using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Noxtend.Api.Workers;
using Noxtend.Application.Common;
using Noxtend.Domain.Job;
using Noxtend.Domain.Ports;
using Noxtend.Infrastructure.Queue;

namespace Noxtend.Tests.Api;

/// <summary>
/// 단계마다 워커가 **실제로** 등록되는가.
///
/// Design Ref: §10.4
///
/// **왜 이 테스트가 있는가.** `AddHostedService` 는 내부에서 `TryAddEnumerable` 을 쓰고,
/// 그것이 구현 타입으로 중복을 제거한다. `TaskWorker` 를 셋 등록하면 첫 하나만 살아남고
/// 나머지는 **로그도 예외도 없이 사라진다.** 실제로 그렇게 됐고, 증상은 "장면 공정만
/// 성공하고 추출이 영원히 대기" 였다 — 큐도 오케스트레이터도 정상인데 아무도 안 듣고
/// 있었다.
///
/// 단계를 넷째로 늘릴 때 같은 실수가 조용히 재발할 수 있으므로 개수로 고정한다.
/// </summary>
public sealed class TaskWorkerRegistrationTests
{
    /// <summary>기본값과 다른 값을 써야 "설정을 실제로 읽는가" 가 검증된다.</summary>
    private const int GenerationWorkers = 2;

    private const int MeshWorkers = 4;

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();

        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<ITaskQueue, InMemoryTaskQueue>();
        services.AddTaskWorkers(
            new GenerationOptions { Workers = GenerationWorkers },
            new MeshGenerationOptions { Workers = MeshWorkers });

        return services.BuildServiceProvider();
    }

    [Fact]
    public void EveryStageGetsItsOwnWorker()
    {
        using var provider = BuildProvider();

        var workers = provider.GetServices<IHostedService>().OfType<TaskWorker>().ToList();

        // **워커를 여럿 두는 단계가 둘이다** (사이클 #7·#10). 그 수가 각 단계의 동시성
        // 상한이고, 나머지는 하나씩이다 — 총합은 "단계 수 - 2 + 생성 + 3D" 다
        var expected = TaskWorkerRegistration.Stages.Length - 2 + GenerationWorkers + MeshWorkers;

        Assert.Equal(expected, workers.Count);
    }

    [Fact]
    public void GenerationGetsAsManyWorkersAsConfigured()
    {
        using var provider = BuildProvider();

        var generation = provider.GetServices<IHostedService>()
            .OfType<TaskWorker>()
            .Count(w => w.Kind == TaskKind.Generate);

        // TaskWorker 는 await foreach 로 순차 소비하므로 워커 하나 = 동시 1건이다.
        // 이 수가 실제로 등록되지 않으면 NFR-09 의 상한이 설정과 다른 값이 된다
        Assert.Equal(GenerationWorkers, generation);
    }

    /// <summary>
    /// 3D 도 설정한 만큼 등록된다.
    ///
    /// 공급자 기본 동시성이 5건이라 이 값이 그것을 넘으면 429 가 늘고, 모자라면
    /// 파츠가 스물일 때 3D 만 줄을 선다 (§8.3).
    /// </summary>
    [Fact]
    public void MeshGetsAsManyWorkersAsConfigured()
    {
        using var provider = BuildProvider();

        var mesh = provider.GetServices<IHostedService>()
            .OfType<TaskWorker>()
            .Count(w => w.Kind == TaskKind.Reconstruct);

        Assert.Equal(MeshWorkers, mesh);
    }

    [Fact]
    public void WorkersCoverExactlyTheDeclaredStages()
    {
        using var provider = BuildProvider();

        var kinds = provider.GetServices<IHostedService>()
            .OfType<TaskWorker>()
            .Select(w => w.Kind)
            .Distinct()
            .OrderBy(k => k)
            .ToArray();

        Assert.Equal(TaskWorkerRegistration.Stages.OrderBy(k => k), kinds);
    }

    [Fact]
    public void DeclaredStagesMatchTheEnum()
    {
        // 단계를 열거형에 더하고 워커 등록을 빠뜨리면 그 단계는 영원히 대기한다.
        // 큐에는 쌓이는데 아무도 안 꺼내므로 화면에는 "실행 중" 으로만 보인다
        Assert.Equal(
            Enum.GetValues<TaskKind>().OrderBy(k => k),
            TaskWorkerRegistration.Stages.OrderBy(k => k));
    }
}
