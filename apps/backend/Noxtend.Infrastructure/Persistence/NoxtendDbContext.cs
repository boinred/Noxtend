using Microsoft.EntityFrameworkCore;
using Noxtend.Domain.Job;
using Noxtend.Domain.Mesh;
using Noxtend.Domain.Provider;
using Noxtend.Domain.Scene;
using Noxtend.Domain.Similarity;
using Noxtend.Domain.Upload;
using Noxtend.Tuning.Domain.Call;
using Noxtend.Tuning.Domain.Golden;
using Noxtend.Tuning.Domain.Prompt;

namespace Noxtend.Infrastructure.Persistence;

/// <summary>
/// Design Ref: §3.3 · §10.4 — one SqlServer migration set, no provider branching.
///
/// The DbContext lives in Infrastructure and never leaves it. Application talks to
/// repository Ports (§3.2), which is what lets the orchestration tests run without
/// a database at all (§2.0).
/// </summary>
public sealed class NoxtendDbContext(DbContextOptions<NoxtendDbContext> options) : DbContext(options)
{
    public DbSet<PipelineJob> Jobs => Set<PipelineJob>();
    public DbSet<StoredImage> StoredImages => Set<StoredImage>();
    public DbSet<ProviderConfig> ProviderConfigs => Set<ProviderConfig>();

    // 외부 3D 제출 추적 (사이클 #10). 작업과 달리 별도 애그리게이트라 자기 DbSet 을 갖는다 —
    // 폴링 checkpoint 가 작업 트랜잭션 밖에서 저장돼야 하기 때문이다 (§3.2)
    public DbSet<MeshRun> MeshRuns => Set<MeshRun>();

    // 3D 조립 명세 (scene-assembly). 작업과 별도 애그리게이트다 — 지연 유도가
    // 작업 트랜잭션 밖에서 저장되고, 낡으면 통째로 다시 만든다 (§3.4)
    public DbSet<SceneLayout> SceneLayouts => Set<SceneLayout>();

    // 유사도 실행 — 제작 파이프라인과 별도 경계 (background-similarity-tuning D-01)
    public DbSet<SimilarityRun> SimilarityRuns => Set<SimilarityRun>();
    public DbSet<SimilarityEvaluation> SimilarityEvaluations => Set<SimilarityEvaluation>();

    // 튜닝 (사이클 #5). 별도 프로젝트의 엔티티지만 DbContext 는 하나다 —
    // 마이그레이션 세트가 둘이면 적용 순서를 사람이 관리하게 된다
    public DbSet<PromptVersion> PromptVersions => Set<PromptVersion>();
    public DbSet<LlmCall> LlmCalls => Set<LlmCall>();
    public DbSet<ModelPrice> ModelPrices => Set<ModelPrice>();
    public DbSet<GoldenSample> GoldenSamples => Set<GoldenSample>();
    public DbSet<Verdict> Verdicts => Set<Verdict>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.ApplyConfigurationsFromAssembly(typeof(NoxtendDbContext).Assembly);
}
