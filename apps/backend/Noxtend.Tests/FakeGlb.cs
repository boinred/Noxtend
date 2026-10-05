using Noxtend.Infrastructure.Mesh;

namespace Noxtend.Tests;

/// <summary>
/// 여러 테스트가 나눠 쓰는 glTF 2.0 바이트.
///
/// **가짜 공급자가 내는 것과 같은 바이트다.** 전에는 여기에 따로 짠 최소 GLB 가 있었고,
/// 그것이 `FakeMeshProvider` 의 것과 조용히 어긋났다. 정의가 둘이면 한쪽만 고쳐도 테스트가
/// 통과하고, 실제로 그랬다 — 둘 다 장면이 없어 뷰어에서 안 열렸다.
/// </summary>
internal static class FakeGlb
{
    public static byte[] Bytes { get; } = FakeMeshProvider.MinimalGlb();
}
