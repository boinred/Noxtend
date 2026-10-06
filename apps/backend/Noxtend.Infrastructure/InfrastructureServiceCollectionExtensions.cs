using Azure.Storage.Blobs;
using Noxtend.Application.Scene;
using Noxtend.Application.Similarity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Noxtend.Application.Common;
using Noxtend.Domain.Common;
using Noxtend.Application.Generation;
using Noxtend.Application.Job;
using Noxtend.Application.Mesh;
using Noxtend.Application.Pipeline;
using Noxtend.Application.Stages;
using Noxtend.Application.Providers;
using Noxtend.Application.Uploads;
using Noxtend.Domain.Ports;
using Noxtend.Infrastructure.Blob;
using Noxtend.Infrastructure.Health;
using Noxtend.Infrastructure.Image;
using Noxtend.Infrastructure.Llm;
using Noxtend.Infrastructure.Mesh;
using Noxtend.Infrastructure.Persistence;
using Noxtend.Infrastructure.Persistence.Repositories;
using Noxtend.Infrastructure.Queue;
using Noxtend.Infrastructure.RateLimit;
using Noxtend.Infrastructure.Scheduling;
using Noxtend.Infrastructure.Security;
using Noxtend.Tuning.Application.Calls;
using Noxtend.Tuning.Application.Prices;
using Noxtend.Tuning.Application.Golden;
using Noxtend.Tuning.Application.Prompts;
using Noxtend.Tuning.Domain.Ports;

namespace Noxtend.Infrastructure;

/// <summary>
/// Design Ref: §9.1 — Api is the composition root; Infrastructure only offers the
/// registration surface. Nothing here reaches back into ASP.NET routing.
/// </summary>
public static class InfrastructureServiceCollectionExtensions
{
    private const string ImageContainerName = "source-images";
    private const string MeshContainerName = "meshes";

    public static IServiceCollection AddNoxtendInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var db = Required(configuration, "ConnectionStrings:Db");
        var blob = Required(configuration, "ConnectionStrings:Blob");
        var redis = Required(configuration, "ConnectionStrings:Redis");

        // 핸들러가 IOptions 가 아니라 JobOptions 를 직접 받는다 — Application 이
        // Microsoft.Extensions.Options 를 알 이유가 없다
        services.AddSingleton(configuration.GetSection("Jobs").Get<JobOptions>() ?? new JobOptions());

        // 생성 공정의 리스·동시성·크기는 텍스트와 자릿수가 달라 값을 나눈다 (§2.3 A-4·A-7·A-8)
        services.AddSingleton(
            configuration.GetSection("Generation").Get<GenerationOptions>() ?? new GenerationOptions());

        // 3D 는 외부 작업이 10~120초라 자릿수가 또 다르다 (사이클 #10 §12.1)
        services.AddSingleton(
            configuration.GetSection("MeshGeneration").Get<MeshGenerationOptions>()
            ?? new MeshGenerationOptions());

        AddPersistence(services, db);
        AddExternalServices(services, blob, redis);
        AddLlm(services, configuration);
        AddProbes(services, db);
        AddUseCases(services);

        return services;
    }

    /// <summary>Design Ref: §2.2 — 키 복호화는 Factory 안에서만 일어난다.</summary>
    private static void AddLlm(IServiceCollection services, IConfiguration configuration)
    {
        // 공급자 호출은 10분 이상 걸릴 수 있다 (§2.2). 기본 100초 타임아웃이면
        // 정상 호출이 취소로 보이고, 그러면 재시도가 비용만 태운다
        // 3D 는 단일 요청 상한이 짧아도 된다 — 오래 걸리는 것은 조회 루프이지
        // 요청 하나가 아니다 (§7.1)
        services.AddHttpClient(MeshProviderFactory.HttpClientName)
            .ConfigureHttpClient(http =>
            {
                http.BaseAddress = new Uri("https://openapi.tripo3d.ai/v3/");
                http.Timeout = TimeSpan.FromSeconds(120);
            });

        services.AddHttpClient(MeshProviderFactory.MeshyHttpClientName)
            .ConfigureHttpClient(http =>
            {
                http.BaseAddress = new Uri("https://api.meshy.ai/openapi/v1/");

                // 입력이 요청 본문에 실려 제출 하나가 수 MB 다 — Tripo 보다 넉넉히 잡는다
                http.Timeout = TimeSpan.FromSeconds(180);
            });

        // 외부 공급자 TLS 연결 풀 순환 — 텍스트·이미지가 같은 설정을 쓴다
        static SocketsHttpHandler ProviderHandler() => new()
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30),
        };

        // 텍스트는 5분이다. 이미지 기준 20분을 함께 쓰던 때, 공급자가 늦게 답하자 워커
        // 한 대가 그만큼 묶여 그 단계 전체가 멈췄다 — 텍스트 단계는 워커가 하나다.
        //
        // 5분인 이유는 실측이다. 정상은 9~74초인데 한 번 461초가 나왔다 — 토큰 수는
        // 정상 호출과 같았으니 공급자 대기 시간이다. 그 값을 살리자고 20분을 두면 막힘이
        // 길어지고, 3분으로 조이면 정상 완료를 자를 위험이 있어 그 사이로 잡았다
        services.AddHttpClient(LlmProviderFactory.HttpClientName)
            .ConfigureHttpClient(http => http.Timeout = TimeSpan.FromMinutes(5))
            .ConfigurePrimaryHttpMessageHandler(ProviderHandler);

        // 이미지는 한 장에 수십 초가 정상이라 넉넉히 둔다 (기존 값 유지)
        services.AddHttpClient(LlmProviderFactory.ImageHttpClientName)
            .ConfigureHttpClient(http => http.Timeout = TimeSpan.FromMinutes(20))
            .ConfigurePrimaryHttpMessageHandler(ProviderHandler);

        var useFake = configuration.GetValue("Llm:UseFake", false);

        // 복호화 지점이 하나다. Factory 와 Catalog 가 이것을 공유하므로
        // "사용 중지 확인" 같은 규칙이 두 곳에서 어긋나지 않는다
        services.AddScoped<ProviderCredentialResolver>();

        // 프롬프트·내역 Port 를 튜닝 저장소로 잇는다 (§2.1) — 여기가 두 컨텍스트가
        // 만나는 유일한 지점이다
        services.AddScoped<IPromptCatalog, TuningPromptCatalog>();
        services.AddScoped<ILlmCallRecorder, TuningLlmCallRecorder>();

        // 어댑터는 순수하게 두고 데코레이터가 내역을 남긴다 (§2.3-4).
        // Factory 가 감싸므로 어댑터는 데코레이터의 존재를 모른다
        services.AddScoped<ILlmProviderFactory>(sp => new LlmProviderFactory(
            sp.GetRequiredService<ProviderCredentialResolver>(),
            sp.GetRequiredService<IHttpClientFactory>(),
            useFake,
            inner => new RecordingLlmProvider(
                inner,
                sp.GetRequiredService<ILlmCallRecorder>(),
                sp.GetRequiredService<ILogger<RecordingLlmProvider>>())));

        // 이미지 경로도 같은 구조다 (사이클 #7 §3.2). 어댑터는 순수하고 데코레이터가
        // 내역을 남기며, 자격증명 해석과 UseFake 스위치를 텍스트와 공유한다 —
        // 스위치를 나누면 "텍스트는 가짜인데 이미지는 진짜" 라는 조합이 생긴다
        services.AddScoped<IImageProviderFactory>(sp => new ImageProviderFactory(
            sp.GetRequiredService<ProviderCredentialResolver>(),
            sp.GetRequiredService<IHttpClientFactory>(),
            useFake,
            inner => new RecordingImageProvider(
                inner,
                sp.GetRequiredService<ILlmCallRecorder>(),
                sp.GetRequiredService<ILogger<RecordingImageProvider>>())));

        // 캐시는 Singleton 이고 Catalog 는 Scoped 다 — 목록이 요청을 넘어 살아남아야
        // 캐시가 의미를 갖는다
        services.AddMemoryCache();

        // 3D 공급자도 같은 스위치를 쓴다 — 텍스트만 가짜이고 3D 만 실과금되는 조합을
        // 만들지 않는다 (§12.2)
        services.AddScoped<IMeshProviderFactory>(sp => new MeshProviderFactory(
            sp.GetRequiredService<ProviderCredentialResolver>(),
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<MeshGenerationOptions>(),
            sp.GetRequiredService<ILoggerFactory>(),
            useFake));

        services.AddScoped<IModelCatalog>(sp => new ModelCatalog(
            sp.GetRequiredService<ProviderCredentialResolver>(),
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<IMemoryCache>(),
            useFake));
    }

    private static void AddPersistence(IServiceCollection services, string connectionString)
    {
        // 마이그레이션 적용 시점에 DB 가 아직 뜨는 중일 수 있다 — 재시도가 기동 순서를
        // 매니페스트에서 강제하지 않아도 되게 한다
        void Configure(DbContextOptionsBuilder options) => options.UseSqlServer(
            connectionString, sql => sql.EnableRetryOnFailure());

        // **옵션만 싱글턴으로 올린다.** 컨텍스트 자체는 그대로 스코프인데, 아래 팩토리가
        // 싱글턴이라 스코프 옵션을 먹을 수 없다 — 그대로 두면 기동이 죽는다
        services.AddDbContext<NoxtendDbContext>(
            Configure, optionsLifetime: ServiceLifetime.Singleton);

        // 3D 실행 checkpoint 는 스코프 컨텍스트를 쓸 수 없다 (§3.2). 공정 실행이 리스 갱신
        // 루프로 그것을 이미 붙들고 있어, 폴링이 같은 컨텍스트에 쓰면 EF 의 동시 사용
        // 금지에 걸린다. 팩토리가 호출마다 짧은 컨텍스트를 연다
        services.AddDbContextFactory<NoxtendDbContext>(Configure);

        services.AddScoped<IJobRepository, EfJobRepository>();
        services.AddScoped<IMeshRunRepository, EfMeshRunRepository>();
        services.AddScoped<ISceneLayoutRepository, EfSceneLayoutRepository>();
        services.AddScoped<ISimilarityRepository, EfSimilarityRepository>();
        services.AddScoped<IStoredImageRepository, EfStoredImageRepository>();
        services.AddScoped<IProviderConfigRepository, EfProviderConfigRepository>();

        // 튜닝 저장소 (사이클 #5). 같은 DbContext 를 공유한다 —
        // 프롬프트 활성 전환이 한 트랜잭션이어야 하므로 스코프가 같아야 한다
        services.AddScoped<IPromptVersionRepository, EfPromptVersionRepository>();
        services.AddScoped<ILlmCallRepository, EfLlmCallRepository>();
        services.AddScoped<IGoldenSampleRepository, EfGoldenSampleRepository>();
        services.AddScoped<IVerdictRepository, EfVerdictRepository>();
        services.AddScoped<IModelPriceRepository, EfModelPriceRepository>();
    }

    private static void AddExternalServices(IServiceCollection services, string blob, string redis)
    {
        // Blob client construction is offline — it parses the connection string only.
        // Reachability is the probe's job (§4.2 #1).
        services.AddSingleton(new BlobServiceClient(blob));
        services.AddSingleton(new RedisConnectionProvider(redis));

        services.AddSingleton<IBlobStorage>(sp => new AzureBlobStorage(
            sp.GetRequiredService<BlobServiceClient>(), ImageContainerName));

        // 3D 산출물은 컨테이너를 나눈다 — 수명 정책과 크기 분포가 이미지와 다르다
        services.AddSingleton<IMeshArtifactStorage>(sp => new AzureMeshArtifactStorage(
            sp.GetRequiredService<BlobServiceClient>(), MeshContainerName));

        // GLB 형태 판독 — 바닥을 덮는 파츠와 서 있는 파츠를 가른다 (#19 후속)
        services.AddSingleton<IMeshExtentsReader, MeshExtentsReader>();

        services.AddSingleton<ITaskQueue, RedisTaskQueue>();

        // 유사도 전용 스트림 (background-similarity-tuning §12) — ITaskQueue 와 분리
        services.AddSingleton<ISimilarityQueue, RedisSimilarityQueue>();
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<ISecretProtector, DataProtectionSecretProtector>();

        // generation-rate-limiting §3① — 워커가 몇 대든 공급자 설정 단위로 공유돼야 하므로 Redis
        services.AddSingleton<IRateLimiter, RedisRateLimiter>();
        services.AddScoped<RateLimitGate>();

        // §3② 후속 — 백오프 시각에 맞춘 재확인 예약 (독립 리뷰 지적: 스위퍼 주기 종속 해소)
        services.AddSingleton<IDelayedActionScheduler, BackgroundDelayedActionScheduler>();
    }

    private static void AddProbes(IServiceCollection services, string db)
    {
        services.AddSingleton<IDependencyProbe>(sp => new SqlServerProbe(
            db, sp.GetRequiredService<ILogger<SqlServerProbe>>()));

        services.AddSingleton<IDependencyProbe>(sp => new RedisProbe(
            sp.GetRequiredService<RedisConnectionProvider>().GetAsync,
            sp.GetRequiredService<ILogger<RedisProbe>>()));

        // 네이티브 이미지 디코더 (사이클 #11). 배포에서만 터지는 결함이 있었고,
        // 증상이 3D 공정에서 나와 원인에서 멀었다 — 기동 직후에 드러나야 할 값이다
        services.AddSingleton<IDependencyProbe>(sp => new ImageDecoderProbe(
            sp.GetRequiredService<IImageTranscoder>(),
            sp.GetRequiredService<ILogger<ImageDecoderProbe>>()));

        services.AddSingleton<IDependencyProbe>(sp => new BlobStorageProbe(
            sp.GetRequiredService<BlobServiceClient>(),
            sp.GetRequiredService<ILogger<BlobStorageProbe>>()));
    }

    /// <summary>
    /// 유스케이스는 Scoped 다. Repository 가 DbContext 를 물고 있으므로 요청 하나가
    /// 하나의 변경 추적 단위를 갖는다.
    /// </summary>
    private static void AddUseCases(IServiceCollection services)
    {
        // 단계 구현. 넷째 단계는 여기 한 줄이 는다 (§2.3 · G-1)
        services.AddScoped<IStage, AnalyzeStage>();
        services.AddScoped<IStage, ExtractStage>();
        services.AddScoped<IStage, DecomposeStage>();
        services.AddScoped<IStage, RewriteDescriptionsStage>();
        services.AddScoped<StageRegistry>();

        services.AddScoped<JobOrchestrator>();
        services.AddScoped<CreateUploadHandler>();
        services.AddScoped<StartJobHandler>();
        services.AddScoped<Noxtend.Application.Sprites.SpriteCommandsHandler>();
        services.AddScoped<Noxtend.Application.Sprites.SpritePackageWriter>();
        services.AddScoped<Noxtend.Application.Sprites.RunSpritePackTaskHandler>();
        services.AddScoped<Noxtend.Application.Sprites.StartSpriteJobHandler>();
        services.AddScoped<Noxtend.Application.Sprites.RunSpriteAnalysisTaskHandler>();
        services.AddScoped<Noxtend.Application.Sprites.RunSpriteGenerationTaskHandler>();

        // 공유 실행 골격 — 텍스트·이미지 핸들러가 함께 쓴다 (§2.0)
        services.AddScoped<TaskExecution>();
        services.AddScoped<RunTaskHandler>();

        // 이미지 경로 (사이클 #7). IImageStage 를 두지 않으므로 구체 클래스를 등록한다 (§2.3 A-1b)
        services.AddScoped<GenerationStage>();
        services.AddScoped<RunGenerationTaskHandler>();

        // Synthesize 는 항상 한 애플리케이션 핸들러 안에서 즉시 끝나 큐에 도달하지 않는다
        // (spec 20260917) — 워커 등록표를 채우기 위한 방어용 핸들러다
        services.AddScoped<UnreachableSynthesizeTaskHandler>();

        // 3D 경로 (사이클 #10). 정규화가 Application 에 있는 것은 검증 규칙이 도메인
        // 지식이기 때문이고, 디코딩만 Port 뒤로 밀어 두었다 (§6.5)
        // 디코딩만 Port 뒤에 있다 — 네이티브 의존이 Application 에 들어오면
        // 유스케이스 테스트가 그 라이브러리를 요구하게 된다
        services.AddSingleton<IImageTranscoder, SkiaImageTranscoder>();
        services.AddScoped<MeshInputNormalizer>();

        // 접수와 뒤늦은 지정이 같은 3D 선택 검증을 쓴다 (사이클 #11 D-11)
        services.AddScoped<MeshSelectionValidator>();
        services.AddScoped<AddMeshProductionHandler>();
        services.AddScoped<RunMeshTaskHandler>();
        services.AddScoped<GetJobHandler>();
        services.AddScoped<GetSceneLayoutHandler>();

        // 유사도 실행 (background-similarity-tuning)
        services.AddScoped<StartSimilarityRunHandler>();
        services.AddScoped<GetSimilarityStatusHandler>();
        services.AddScoped<GetSimilarityRunHandler>();
        services.AddScoped<EvaluateSimilarityHandler>();
        services.AddScoped<CreateSimilarityCandidateHandler>();
        services.AddScoped<UploadCandidateRenderHandler>();
        services.AddScoped<ListSceneRevisionsHandler>();
        services.AddScoped<RestoreSceneRevisionHandler>();
        services.AddScoped<RetrySimilarityRunHandler>();
        services.AddScoped<CancelSimilarityRunHandler>();
        services.AddScoped<CompleteSimilarityRunHandler>();
        services.AddScoped<SweepSimilarityHandler>();
        services.AddScoped<ListSimilarityRunsHandler>();
        services.AddScoped<ListJobsHandler>();
        services.AddScoped<CancelJobHandler>();
        services.AddScoped<DeleteJobHandler>();
        services.AddScoped<RetryTaskHandler>();
        services.AddScoped<SweepStaleTasksHandler>();

        // review-gate — 검수 게이트
        services.AddScoped<GetReviewHandler>();
        services.AddScoped<AddReviewPartHandler>();
        services.AddScoped<FindOverlapsHandler>();
        services.AddScoped<RemoveReviewPartHandler>();
        services.AddScoped<MoveReviewPlacementHandler>();
        services.AddScoped<ApproveReviewHandler>();
        services.AddScoped<ReviewDescriptionsHandler>();
        services.AddScoped<GenerateSelectedViewsHandler>();
        services.AddScoped<ReturnToDescriptionsFromGenerationHandler>();
        services.AddScoped<ReplanPartMeshHandler>();

        services.AddScoped<ListProvidersHandler>();
        services.AddScoped<CreateProviderHandler>();
        services.AddScoped<UpdateProviderHandler>();
        services.AddScoped<DeleteProviderHandler>();
        services.AddScoped<ListProviderModelsHandler>();
        services.AddScoped<TestProviderHandler>();

        AddTuningUseCases(services);
    }

    /// <summary>
    /// Design Ref: §2.4 — 튜닝 유스케이스.
    ///
    /// 파이프라인 유스케이스와 같은 컨테이너에 등록되지만, **서로를 주입받을 수 없다** —
    /// 프로젝트 참조가 없어 타입이 보이지 않는다. 경계를 컴파일러가 지킨다.
    /// </summary>
    private static void AddTuningUseCases(IServiceCollection services)
    {
        services.AddScoped<ListActivePromptsHandler>();
        services.AddScoped<GetPromptGridHandler>();
        services.AddScoped<ListPromptVersionsHandler>();
        services.AddScoped<CreatePromptVersionHandler>();
        services.AddScoped<ActivatePromptVersionHandler>();

        services.AddScoped<ListGoldenSamplesHandler>();
        services.AddScoped<CreateGoldenSampleHandler>();
        services.AddScoped<UpdateGoldenSampleHandler>();
        services.AddScoped<DeleteGoldenSampleHandler>();
        services.AddScoped<RecordVerdictHandler>();
        services.AddScoped<EstimateSimilarityCostHandler>();

        services.AddScoped<ListJobCallsHandler>();
        services.AddScoped<ListModelPricesHandler>();
        services.AddScoped<CreateModelPriceHandler>();
        services.AddScoped<UpdateModelPriceHandler>();
        services.AddScoped<DeleteModelPriceHandler>();
        services.AddScoped<GetCallStatsHandler>();
    }

    /// <summary>
    /// Missing connection strings fail at startup rather than surfacing as a confusing
    /// 503 later. A misconfigured deployment is a deployment error, not a dependency outage.
    /// </summary>
    private static string Required(IConfiguration configuration, string key)
        => configuration[key]
           ?? throw new InvalidOperationException(
               $"Missing configuration '{key}'. See deploy/k8s/secrets.example.yaml.");
}
