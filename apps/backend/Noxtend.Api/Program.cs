using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Noxtend.Api.Workers;
using Noxtend.Application.Common;
using Noxtend.Domain.Job;
using Noxtend.Domain.Ports;
using Noxtend.Infrastructure;
using Noxtend.Infrastructure.Persistence;

// Design Ref: §9.1 — Api is the composition root. No use case, no entity, no adapter
// is constructed anywhere else.
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers().AddJsonOptions(options =>
{
    // 기본 인코더는 비ASCII 를 \uXXXX 로 escape 한다. 일관성 프롬프트와 파츠 이름이
    // 전부 한국어라 (§2.2) 한 글자가 6바이트가 되고, 폴링 응답이 그만큼 무거워진다.
    // Relaxed 가 위험해지는 것은 HTML 에 직접 끼워 넣을 때인데, 여기 응답은 JSON 으로만 소비된다
    options.JsonSerializerOptions.Encoder = System.Text.Encodings.Web.JavaScriptEncoder
        .UnsafeRelaxedJsonEscaping;
});
builder.Services.AddOpenApi();
builder.Services.AddNoxtendInfrastructure(builder.Configuration);

// Design Ref: §10.4 — 단계 추가는 TaskKind + 워커 + 스트림 세 가지로 끝나야 한다.
//
// 사이클 #5 가 그 주장을 검증했다: 단계가 하나에서 셋이 되면서 바뀐 것은 이 배열과
// TaskKind 열거형뿐이고, TaskWorker · 오케스트레이터 · 스위퍼 코드는 그대로다.
// (스위퍼는 의존 확인이 빠져 있던 결함을 한 줄 고쳤다 — 단계 수와는 무관한 버그였다)
//
// 사이클 #7 은 여기에 넷째 단계를 더하고, 생성만 워커를 여럿 등록한다 —
// 그 수가 팬아웃 동시성 상한이다 (§2.3 A-4)
builder.Services.AddTaskWorkers(
    builder.Configuration.GetSection("Generation").Get<GenerationOptions>(),
    builder.Configuration.GetSection("MeshGeneration").Get<MeshGenerationOptions>());

builder.Services.AddHostedService<TaskSweeper>();

// 유사도 평가 워커 — 전용 스트림 소비 (background-similarity-tuning §12)
builder.Services.AddHostedService<SimilarityWorker>();
builder.Services.AddHostedService<SimilaritySweeper>();

// The dev server runs on a different origin than the API. Without this the browser
// blocks every call and the failure looks like a backend outage from the UI side.
const string DevCorsPolicy = "dev";
builder.Services.AddCors(options => options.AddPolicy(DevCorsPolicy, policy => policy
    .WithOrigins("http://localhost:5173", "http://localhost:4173")
    .AllowAnyHeader()
    .AllowAnyMethod()));

// Design Ref: §7 — the key ring must outlive the pod. Regenerating it on restart makes
// every stored provider key undecryptable, which surfaces as data loss rather than a
// configuration mistake. Path comes from the mounted volume in deploy/k8s/api.yaml.
var dataProtection = builder.Services.AddDataProtection().SetApplicationName("Noxtend");
var keysPath = builder.Configuration["DataProtection:KeysPath"];
if (!string.IsNullOrWhiteSpace(keysPath))
{
    dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keysPath));
}

var app = builder.Build();

// Plan §4.1 SC: 매니페스트 적용 한 번으로 로컬 전체 기동.
// A separate migration job would mean the stack is not up until a human runs a second
// step. Migrate() is idempotent, and the single-replica Deployment (§deploy/k8s/api.yaml)
// removes the concurrent-migration hazard that would otherwise argue against this.
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<NoxtendDbContext>();
    await db.Database.MigrateAsync();
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseCors(DevCorsPolicy);
}

// No HTTPS redirection: the container serves plain HTTP on 8080 and TLS terminates
// upstream. Redirecting here would break every in-cluster call, /health included.
app.MapControllers();

app.Run();

public partial class Program { }
