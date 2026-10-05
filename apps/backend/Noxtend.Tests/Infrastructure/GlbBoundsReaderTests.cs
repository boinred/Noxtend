using System.Buffers.Binary;
using System.Text;
using Noxtend.Domain.Scene;
using Noxtend.Infrastructure.Mesh;

namespace Noxtend.Tests.Infrastructure;

/// <summary>
/// GLB 월드 바운딩 박스. Design Ref: background-placement-projection(#19) 후속
///
/// **뷰어와 같은 값이어야 한다.** 뷰어는 `Box3.setFromObject` 로 노드 변환까지 적용한
/// 박스를 재고 그 높이로 정규화한다. 서버가 accessor 의 min·max 만 보면 회전이 붙은
/// 노드에서 축이 뒤바뀌어 "판인가 서 있는가" 판정이 반대로 뒤집힌다.
/// </summary>
public sealed class GlbBoundsReaderTests
{
    [Fact]
    public void Read_MeasuresAPlainAccessorBox()
    {
        var glb = Glb("""
            {"asset":{"version":"2.0"},"scene":0,"scenes":[{"nodes":[0]}],
             "nodes":[{"mesh":0}],
             "meshes":[{"primitives":[{"attributes":{"POSITION":0}}]}],
             "accessors":[{"min":[-2,0,-3],"max":[2,1,3]}]}
            """);

        Assert.Equal(new MeshExtents(4, 1, 6), GlbBoundsReader.Read(glb));
    }

    /// <summary>
    /// **회전이 축을 바꾼다.** 90° 로 누운 노드에서 accessor 만 보면 세로가 1 인데
    /// 실제로는 6 이다 — 판정이 정확히 반대로 뒤집히는 경우다.
    /// </summary>
    [Fact]
    public void Read_AppliesNodeRotation()
    {
        // x 축 −90° 회전 (z 가 y 로 온다)
        var glb = Glb("""
            {"asset":{"version":"2.0"},"scene":0,"scenes":[{"nodes":[0]}],
             "nodes":[{"mesh":0,"rotation":[-0.7071068,0,0,0.7071068]}],
             "meshes":[{"primitives":[{"attributes":{"POSITION":0}}]}],
             "accessors":[{"min":[-2,0,-3],"max":[2,1,3]}]}
            """);

        var extents = GlbBoundsReader.Read(glb)!;

        Assert.Equal(4, extents.X, precision: 5);
        Assert.Equal(6, extents.Y, precision: 5);
        Assert.Equal(1, extents.Z, precision: 5);
    }

    [Fact]
    public void Read_AppliesNodeScaleAndTranslation()
    {
        var glb = Glb("""
            {"asset":{"version":"2.0"},"scene":0,"scenes":[{"nodes":[0]}],
             "nodes":[{"mesh":0,"scale":[2,3,4],"translation":[10,0,0]}],
             "meshes":[{"primitives":[{"attributes":{"POSITION":0}}]}],
             "accessors":[{"min":[0,0,0],"max":[1,1,1]}]}
            """);

        Assert.Equal(new MeshExtents(2, 3, 4), GlbBoundsReader.Read(glb));
    }

    /// <summary>자식 노드는 부모 변환을 물려받는다.</summary>
    [Fact]
    public void Read_AccumulatesParentTransforms()
    {
        var glb = Glb("""
            {"asset":{"version":"2.0"},"scene":0,"scenes":[{"nodes":[0]}],
             "nodes":[{"children":[1],"scale":[2,2,2]},{"mesh":0}],
             "meshes":[{"primitives":[{"attributes":{"POSITION":0}}]}],
             "accessors":[{"min":[0,0,0],"max":[1,1,1]}]}
            """);

        Assert.Equal(new MeshExtents(2, 2, 2), GlbBoundsReader.Read(glb));
    }

    /// <summary>여러 primitive 는 합집합이다.</summary>
    [Fact]
    public void Read_UnionsEveryPrimitive()
    {
        var glb = Glb("""
            {"asset":{"version":"2.0"},"scene":0,"scenes":[{"nodes":[0]}],
             "nodes":[{"mesh":0}],
             "meshes":[{"primitives":[{"attributes":{"POSITION":0}},
                                      {"attributes":{"POSITION":1}}]}],
             "accessors":[{"min":[0,0,0],"max":[1,1,1]},
                          {"min":[-5,0,0],"max":[0,2,0]}]}
            """);

        Assert.Equal(new MeshExtents(6, 2, 1), GlbBoundsReader.Read(glb));
    }

    /// <summary>읽지 못하면 null — 형태를 모르면 서 있는 것으로 다룬다.</summary>
    [Theory]
    [InlineData("not a glb at all")]
    [InlineData("""{"asset":{"version":"2.0"}}""")]
    public void Read_ReturnsNothingWhenItCannotTell(string payload)
        => Assert.Null(GlbBoundsReader.Read(
            payload.StartsWith('{') ? Glb(payload) : Encoding.UTF8.GetBytes(payload)));

    /// <summary>실제 공급자 산출물의 최소 형태 — 가짜 공급자가 내는 glTF 도 읽힌다.</summary>
    [Fact]
    public void Read_MeasuresTheFakeProviderOutput()
    {
        var glb = FakeMeshProviderGlb();

        var extents = GlbBoundsReader.Read(glb);

        Assert.NotNull(extents);
        Assert.Equal(1, extents!.X, precision: 5);
        Assert.Equal(1, extents.Y, precision: 5);
    }

    // ─── 판정 (MeshExtents) ───

    [Theory]
    [InlineData(10, 0.5, 10, true)]    // 포장 — 바닥을 덮는다
    [InlineData(2, 8, 2, false)]       // 탑 — 서 있다
    [InlineData(1, 1, 1, false)]       // 정육면체 — 서 있는 쪽
    [InlineData(0, 0, 0, false)]       // 모르면 서 있는 것으로
    public void IsGroundPlane_SeparatesFlatFromUpright(
        double x, double y, double z, bool expected)
        => Assert.Equal(expected, new MeshExtents(x, y, z).IsGroundPlane);

    /// <summary>바닥 폭을 원하는 값으로 맞추려면 높이 비율만큼 곱해야 한다.</summary>
    [Fact]
    public void HeightPerFootprint_ConvertsFootprintIntoScale()
    {
        var slab = new MeshExtents(20, 1, 10);

        // 바닥 폭 30m 를 원하면 scale = 30 × (1/20) = 1.5
        Assert.Equal(0.05, slab.HeightPerFootprint, precision: 9);
        Assert.Equal(1.5, 30 * slab.HeightPerFootprint, precision: 9);
    }

    private static byte[] Glb(string json)
    {
        var payload = Pad(Encoding.UTF8.GetBytes(json), (byte)' ');
        var glb = new byte[12 + 8 + payload.Length];

        "glTF"u8.CopyTo(glb);
        BinaryPrimitives.WriteUInt32LittleEndian(glb.AsSpan(4), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(glb.AsSpan(8), (uint)glb.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(glb.AsSpan(12), (uint)payload.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(glb.AsSpan(16), 0x4E4F534A);
        payload.CopyTo(glb.AsSpan(20));

        return glb;
    }

    private static byte[] FakeMeshProviderGlb() => Glb("""
        {"asset":{"version":"2.0"},"scene":0,"scenes":[{"nodes":[0]}],
         "nodes":[{"mesh":0}],
         "meshes":[{"primitives":[{"attributes":{"POSITION":0}}]}],
         "accessors":[{"bufferView":0,"componentType":5126,"count":3,"type":"VEC3",
                       "min":[0,0,0],"max":[1,1,0]}]}
        """);

    private static byte[] Pad(byte[] bytes, byte filler)
    {
        var remainder = bytes.Length % 4;
        if (remainder == 0)
        {
            return bytes;
        }

        var padded = new byte[bytes.Length + (4 - remainder)];
        bytes.CopyTo(padded, 0);
        Array.Fill(padded, filler, bytes.Length, padded.Length - bytes.Length);

        return padded;
    }
}
