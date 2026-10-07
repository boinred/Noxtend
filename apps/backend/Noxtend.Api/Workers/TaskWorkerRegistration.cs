using Noxtend.Application.Common;
using Noxtend.Application.Generation;
using Noxtend.Application.Mesh;
using Noxtend.Application.Pipeline;
using Noxtend.Domain.Job;
using Noxtend.Domain.Ports;

namespace Noxtend.Api.Workers;

/// <summary>
/// 단계마다 워커 하나.
///
/// Design Ref: §10.4 — 단계 추가는 <see cref="TaskKind"/> + 워커 + 스트림 세 가지로
/// 끝나야 한다. 사이클 #5 가 그 주장을 검증했다: 단계가 하나에서 셋이 되면서 바뀐 것은
/// <see cref="Stages"/> 배열과 열거형뿐이고 <c>TaskWorker</c> 코드는 그대로다.
///
/// **`AddHostedService` 를 쓰지 않는 것이 핵심이다.** 그 확장은 내부에서
/// <c>TryAddEnumerable</c> 을 쓰는데, 이것이 **구현 타입으로 중복을 제거한다.**
/// <c>TaskWorker</c> 를 셋 등록하면 첫 하나만 살아남고 나머지는 조용히 사라진다 —
/// 로그도 예외도 없다. 실제로 장면 공정만 돌고 추출·분해가 영원히 대기하는 것으로
/// 드러났다. <c>AddSingleton&lt;IHostedService&gt;</c> 는 그 중복 제거를 거치지 않는다.
/// </summary>
public static class TaskWorkerRegistration
{
    /// <summary>
    /// 단계마다 그 단계를 처리하는 핸들러.
    ///
    /// **사이클 #7 에서 값이 아니라 짝이 됐다.** Option B 가 실행 경로를 나누므로 단계가
    /// 어느 핸들러로 가는지가 등록의 일부다 (§2.0). 워커 클래스는 여전히 하나다.
    /// </summary>
    private static readonly Dictionary<TaskKind, Func<IServiceProvider, ITaskHandler>> Handlers = new()
    {
        [TaskKind.PackSprites] = sp => sp.GetRequiredService<Noxtend.Application.Sprites.RunSpritePackTaskHandler>(),
        [TaskKind.GenerateSprite] = sp => sp.GetRequiredService<Noxtend.Application.Sprites.RunSpriteGenerationTaskHandler>(),
        [TaskKind.AnalyzeSprites] = sp => sp.GetRequiredService<Noxtend.Application.Sprites.RunSpriteAnalysisTaskHandler>(),
        [TaskKind.Analyze] = sp => sp.GetRequiredService<RunTaskHandler>(),
        [TaskKind.Extract] = sp => sp.GetRequiredService<RunTaskHandler>(),
        [TaskKind.Decompose] = sp => sp.GetRequiredService<RunTaskHandler>(),

        // occludedby-recompute — 텍스트 경로다. 서술만 묻고 좌표는 묻지 않는다
        [TaskKind.RewriteDescriptions] = sp => sp.GetRequiredService<RunTaskHandler>(),

        // 사이클 #7 — 이미지 경로는 별도 핸들러다 (§2.0 Option B)
        [TaskKind.Generate] = sp => sp.GetRequiredService<RunGenerationTaskHandler>(),

        // 사이클 #10 — 3D 경로도 마찬가지다. 외부 작업이 비동기라 실행 모양이 다르다
        [TaskKind.Reconstruct] = sp => sp.GetRequiredService<RunMeshTaskHandler>(),

        // 대칭(합성) 이미지 생성 (spec 20260917) — 외부 호출이 없어 애플리케이션 핸들러가
        // 한 트랜잭션으로 즉시 끝낸다. 큐에 도달할 일이 없으므로 방어용 핸들러만 둔다
        [TaskKind.Synthesize] = sp => sp.GetRequiredService<UnreachableSynthesizeTaskHandler>(),
    };

    /// <summary>실행되는 단계. 넷째 단계는 위 표에 행 하나가 는다.</summary>
    public static TaskKind[] Stages => [.. Handlers.Keys];

    public static IServiceCollection AddTaskWorkers(
        this IServiceCollection services,
        GenerationOptions? generation = null,
        MeshGenerationOptions? mesh = null)
    {
        // 생성만 워커를 여럿 등록한다 — **이것이 팬아웃 동시성 상한이다** (§2.3 A-4 · NFR-09).
        // TaskWorker 는 await foreach 로 순차 소비하므로 워커 하나 = 동시 1건이고,
        // 파츠가 20개여도 큐에 쌓일 뿐 공급자 동시 호출은 이 수를 넘지 않는다
        var generationWorkers = Math.Max(1, (generation ?? new GenerationOptions()).Workers);

        // 3D 도 여럿이다. 공급자 기본 동시성이 5건이라 여유를 두고 3으로 시작한다 (§8.3)
        var meshWorkers = Math.Max(1, (mesh ?? new MeshGenerationOptions()).Workers);

        foreach (var (kind, resolve) in Handlers)
        {
            var count = kind switch
            {
                TaskKind.Generate or TaskKind.GenerateSprite => generationWorkers,
                TaskKind.Reconstruct => meshWorkers,
                _ => 1,
            };

            for (var i = 0; i < count; i++)
            {
                services.AddSingleton<IHostedService>(sp => new TaskWorker(
                    kind,
                    resolve,
                    sp.GetRequiredService<IServiceScopeFactory>(),
                    sp.GetRequiredService<ITaskQueue>(),
                    sp.GetRequiredService<ILogger<TaskWorker>>()));
            }
        }

        return services;
    }
}
