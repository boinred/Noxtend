using Noxtend.Domain.Job;

namespace Noxtend.Tests.Domain;

/// <summary>Design Ref: review-gate 결정로그 T-03 — 겹친 넓이 ÷ 더 작은 박스 넓이.</summary>
public sealed class BoundsTests
{
    // 벨트(작음)가 바지(큼) 안에 거의 다 걸쳐있다 — 작은 쪽 기준이라 확실히 잡힌다
    [Fact]
    public void OverlapCoefficient_SmallPartMostlyInsideLargePart_IsHigh()
    {
        var belt = new Bounds(X: 0.3, Y: 0.5, W: 0.1, H: 0.05);   // 넓이 0.005
        var pants = new Bounds(X: 0.2, Y: 0.4, W: 0.4, H: 0.5);   // 넓이 0.2, 벨트를 완전히 포함

        var coefficient = belt.OverlapCoefficient(pants);

        // 벨트가 통째로 바지 안에 들어가므로 작은 쪽(벨트) 기준 비율은 1에 가깝다
        Assert.True(coefficient > 0.9, $"expected > 0.9, got {coefficient}");
    }

    // 목걸이와 옷깃처럼 경계만 살짝 스치는 정상적인 인접 관계는 낮게 나와야 한다
    [Fact]
    public void OverlapCoefficient_EdgeTouchOnly_IsLow()
    {
        var necklace = new Bounds(X: 0.4, Y: 0.2, W: 0.1, H: 0.1);   // 넓이 0.01
        var collar = new Bounds(X: 0.45, Y: 0.28, W: 0.2, H: 0.2);   // 경계만 살짝 겹침

        var coefficient = necklace.OverlapCoefficient(collar);

        Assert.True(coefficient < 0.15, $"expected < 0.15, got {coefficient}");
    }

    // 전혀 안 겹치면 0
    [Fact]
    public void OverlapCoefficient_NoIntersection_IsZero()
    {
        var a = new Bounds(X: 0.1, Y: 0.1, W: 0.1, H: 0.1);
        var b = new Bounds(X: 0.5, Y: 0.5, W: 0.1, H: 0.1);

        Assert.Equal(0, a.OverlapCoefficient(b));
    }

    // IoU(합집합 기준)라면 못 잡는 벨트-바지 같은 크기 차이 큰 쌍도, 작은 쪽 기준이면 놓치지 않는다
    [Fact]
    public void OverlapCoefficient_IsSymmetric()
    {
        var small = new Bounds(X: 0.3, Y: 0.5, W: 0.1, H: 0.05);
        var large = new Bounds(X: 0.2, Y: 0.4, W: 0.4, H: 0.5);

        Assert.Equal(small.OverlapCoefficient(large), large.OverlapCoefficient(small), precision: 10);
    }
}
