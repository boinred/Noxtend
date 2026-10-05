using Noxtend.Domain.Job;

namespace Noxtend.Tests.Domain;

/// <summary>
/// 공정이 모델을 소유한다.
///
/// 이전에는 `ProviderConfig.Model` 하나뿐이라 "공급자 하나 = 모델 하나" 였고,
/// 같은 키로 Opus·Sonnet 을 비교하려면 공급자를 두 번 등록해야 했다.
/// §2.3 이 이미 `Task.ProviderConfigId` 를 공정에 뒀으므로 — 단계마다 다른 공급자를
/// 허용하려고 — 모델도 같은 자리에 두는 것이 그 방향과 일관된다.
/// 분해 단계가 붙으면 "추출은 Opus, 생성은 Sonnet" 이 추가 변경 없이 된다.
/// </summary>
public sealed class TaskModelTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);

    private static PipelineJob NewJob()
        => PipelineJob.Create(AssetCategory.Background, Guid.NewGuid(), Now);

    [Fact]
    public void PlanTask_RecordsTheModel()
    {
        var job = NewJob();
        var providerId = Guid.NewGuid();

        var task = job.PlanTask(TaskKind.Extract, 0, providerConfigId: providerId, model: "claude-opus-5");

        Assert.Equal("claude-opus-5", task.Model);
        Assert.Equal(providerId, task.ProviderConfigId);
    }

    [Fact]
    public void PlanTask_AllowsDifferentModelsPerTask()
    {
        // 미래 대비 — 단계마다 다른 모델을 쓰는 것이 구조적으로 가능해야 한다
        var job = NewJob();

        var first = job.PlanTask(TaskKind.Extract, 0, providerConfigId: Guid.NewGuid(), model: "claude-opus-5");
        var second = job.PlanTask(
            TaskKind.Extract, 1, dependsOnTaskId: first.Id,
            providerConfigId: Guid.NewGuid(), model: "claude-sonnet-5");

        Assert.NotEqual(first.Model, second.Model);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void PlanTask_RejectsMissingModel(string? model)
    {
        // 모델 없이 계획된 공정은 워커가 집는 순간 실패한다.
        // 접수 시점에 막아야 사용자가 이유를 아는 자리에서 알게 된다
        var job = NewJob();

        Assert.Throws<ArgumentException>(() =>
            job.PlanTask(TaskKind.Extract, 0, providerConfigId: Guid.NewGuid(), model: model!));
    }

    [Fact]
    public void PlanTask_RejectsModelWithoutProvider()
    {
        // 반대 방향도 막는다 — 부를 공급자가 없는데 모델만 있으면 의미가 없다
        var job = NewJob();

        Assert.Throws<ArgumentException>(() =>
            job.PlanTask(TaskKind.Extract, 0, providerConfigId: null, model: "claude-opus-5"));
    }

    [Fact]
    public void PlanTask_AllowsNeitherProviderNorModel()
    {
        // LLM 을 부르지 않는 미래의 단계 (예: 이미지 합성) 는 둘 다 없다
        var job = NewJob();

        var task = job.PlanTask(TaskKind.Extract, 0);

        Assert.Null(task.ProviderConfigId);
        Assert.Null(task.Model);
    }

    [Fact]
    public void PlanTask_TrimsTheModel()
    {
        var job = NewJob();

        var task = job.PlanTask(
            TaskKind.Extract, 0, providerConfigId: Guid.NewGuid(), model: "  claude-opus-5  ");

        Assert.Equal("claude-opus-5", task.Model);
    }
}
