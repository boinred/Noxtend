using System.Collections.Concurrent;
using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Noxtend.Infrastructure.Persistence;

namespace Noxtend.Tests.Api;

public sealed class ModelPriceUpdateAcceptanceHost(string connectionString) : WebApplicationFactory<Program>
{
    public AcceptanceClock Clock { get; } = new();
    public string Scenario { get; set; } = "normal";
    public string? ScenarioProvider { get; set; }
    public ConcurrentBag<string> OutboundPaths { get; } = [];

    public static string FixtureRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null)
            {
                var path = Path.Combine(directory.FullName, "docs/evals/model-price-update/fixtures");
                if (Directory.Exists(path)) return path;
                directory = directory.Parent;
            }
            throw new DirectoryNotFoundException("Model price acceptance fixtures missing");
        }
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        // 최소 호스트의 DI 등록보다 먼저 연결 설정 제공
        builder.ConfigureHostConfiguration(config => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Db"] = connectionString,
            ["ConnectionStrings:Redis"] = "127.0.0.1:1,abortConnect=false",
            ["ConnectionStrings:Blob"] = "UseDevelopmentStorage=true",
            ["Llm:UseFake"] = "false",
        }));
        return base.CreateHost(builder);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Db"] = connectionString,
            ["ConnectionStrings:Redis"] = "127.0.0.1:1,abortConnect=false",
            ["ConnectionStrings:Blob"] = "UseDevelopmentStorage=true",
            ["Llm:UseFake"] = "false",
            ["DataProtection:KeysPath"] = null,
            ["Logging:LogLevel:Default"] = "Warning",
        }));
        builder.ConfigureServices(services =>
        {
            // 단가 수집 밖의 Worker·키 파일 쓰기 차단
            services.RemoveAll<IHostedService>();
            // 환경 변수와 무관한 일회용 SQL 연결 우선
            services.AddDbContext<NoxtendDbContext>(options => options.UseSqlServer(connectionString));
            services.AddDataProtection().UseEphemeralDataProtectionProvider();
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
            services.ConfigureAll<HttpClientFactoryOptions>(options =>
                options.HttpMessageHandlerBuilderActions.Add(handler =>
                    handler.PrimaryHandler = new FixtureHandler(this)));
        });
    }

    private sealed class FixtureHandler(ModelPriceUpdateAcceptanceHost host) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var uri = request.RequestUri!;
            host.OutboundPaths.Add(uri.Host + uri.AbsolutePath);
            if (request.Method != HttpMethod.Get)
                throw new InvalidOperationException("Acceptance forbids generation, uploads, and paid calls");
            var provider = uri.Host switch
            {
                "api.openai.com" or "developers.openai.com" => "openai",
                "api.anthropic.com" or "platform.claude.com" => "anthropic",
                "generativelanguage.googleapis.com" or "ai.google.dev" => "google",
                "developers.tripo3d.ai" => "tripo",
                "docs.meshy.ai" => "meshy",
                _ => throw new InvalidOperationException("Acceptance outbound host is not in official fixture registry"),
            };
            string file;
            var modelsApi = uri.Host is "api.openai.com" or "api.anthropic.com" or "generativelanguage.googleapis.com";
            if (modelsApi)
            {
                if (!uri.AbsolutePath.EndsWith("/models"))
                    throw new InvalidOperationException("Acceptance only permits official list-model endpoints");
                var secondPage = uri.Query.Contains("after_id=") || uri.Query.Contains("pageToken=");
                var apiKey = request.Headers.TryGetValues("x-api-key", out var keys) ? keys.Single() : null;
                if (provider == "anthropic" && host.Scenario.StartsWith("union-"))
                {
                    file = apiKey switch
                    {
                        "acceptance-key-union-a" => "anthropic-models-union-a.json",
                        "acceptance-key-union-b" when host.Scenario == "union-models-denied" || host.Scenario == "union-models-partial" && secondPage
                            => "anthropic-models-union-denied.json",
                        "acceptance-key-union-b" when host.Scenario == "union-models-partial"
                            => "anthropic-models-union-b-partial.json",
                        "acceptance-key-union-b" => "anthropic-models-union-b-complete.json",
                        _ => throw new InvalidOperationException("Synthetic model union needs a distinct registered test credential"),
                    };
                    var result = new HttpResponseMessage(file.EndsWith("-denied.json") ? HttpStatusCode.Unauthorized : HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(File.ReadAllBytes(Path.Combine(FixtureRoot, file))),
                    };
                    result.Content.Headers.ContentType = new("application/json");
                    return Task.FromResult(result);
                }
                if (provider == host.ScenarioProvider &&
                    (host.Scenario == "models-denied" || host.Scenario == "models-partial" && secondPage))
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
                    {
                        Content = new StringContent("{\"error\":{\"message\":\"synthetic denial\"}}"),
                    });
                file = provider == "openai" ? "openai-models.json"
                    : $"{provider}-models-page-{(secondPage ? 2 : 1)}.json";
            }
            else
            {
                file = uri.AbsolutePath switch
                {
                    "/api/docs/pricing" or "/docs/en/about-claude/pricing" or "/gemini-api/docs/pricing" or "/en/pricing" or "/en/api/pricing"
                        => $"{provider}-pricing.html",
                    "/en/models" => "tripo-models.html",
                    "/en/models/p1" => "tripo-p1.html",
                    "/openapi.json" => "meshy-openapi.json",
                    "/en/api/multi-image-to-3d" => "meshy-multi-image.html",
                    _ => throw new InvalidOperationException("Acceptance URL has no recorded fixture"),
                };
                if ((file.EndsWith("-pricing.html") || file == "tripo-p1.html") && provider == host.ScenarioProvider &&
                    host.Scenario is "structure-changed" or "unit-missing" or "future")
                    file = file.Replace(".html", $"-{host.Scenario}.html");
                if (file.EndsWith("-pricing.html") && provider == host.ScenarioProvider && host.Scenario == "pricing-failed")
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            }
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(File.ReadAllBytes(Path.Combine(FixtureRoot, file))),
            };
            response.Content.Headers.ContentType = new(file.EndsWith(".json") ? "application/json" : "text/html");
            return Task.FromResult(response);
        }
    }
}

public sealed class AcceptanceClock : TimeProvider
{
    private DateTimeOffset now = new(2026, 10, 9, 0, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => now;
    public void Advance(TimeSpan duration) => now += duration;
}
