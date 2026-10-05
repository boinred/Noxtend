using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Noxtend.Domain.Mesh;
using Noxtend.Infrastructure.Blob;
using Noxtend.Infrastructure.Mesh;

namespace Noxtend.Tests.Infrastructure;

/// <summary>
/// 가짜 공급자가 내는 산출물이 실제로 쓸 수 있는 것인가.
///
/// **형식 검증만 통과하는 것으로는 부족하다는 것이 이 검사의 이유다.** 전에는 `asset` 만
/// 담은 GLB 를 냈고, 그것은 우리 저장소 검증(magic·버전·길이)을 통과하면서도 뷰어에서는
/// 열리지 않았다 — "Model does not have a scene". 로컬에서 `Llm:UseFake` 로 파이프라인을
/// 돌리면 3D 가 "불러오지 못했습니다" 로 나왔다.
///
/// 검증을 통과하는 것과 열리는 것은 다르다. 여기서 보는 것은 뒤쪽이다.
/// </summary>
public sealed class FakeMeshProviderTests
{
    [Fact]
    public void Glb_HasTheHeaderOurStorageValidates()
    {
        var glb = FakeMeshProvider.MinimalGlb();

        Assert.Equal("glTF"u8.ToArray(), glb[..4]);
        Assert.Equal(2u, BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(4, 4)));

        // 헤더가 말하는 길이와 실제 바이트가 다르면 저장소가 "내려받다 끊겼다" 로 읽는다
        Assert.Equal((uint)glb.Length, BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(8, 4)));
    }

    /// <summary>
    /// **장면과 mesh 가 둘 다 있어야 열린다.**
    ///
    /// `asset` 만 있으면 "Model does not have a scene" 으로, 장면만 있고 비어 있으면
    /// `loadfailure` 로 거절당한다 — 둘 다 실측으로 확인했다.
    /// </summary>
    [Fact]
    public void Glb_CarriesASceneWithAMesh()
    {
        using var document = JsonDocument.Parse(JsonChunkOf(FakeMeshProvider.MinimalGlb()));
        var root = document.RootElement;

        Assert.Equal(0, root.GetProperty("scene").GetInt32());

        var scene = root.GetProperty("scenes").EnumerateArray().Single();
        Assert.NotEmpty(scene.GetProperty("nodes").EnumerateArray());

        // 노드가 가리키는 mesh 가 실제로 있어야 한다 — 참조만 있고 대상이 없으면 못 연다
        Assert.NotEmpty(root.GetProperty("meshes").EnumerateArray());
        Assert.NotEmpty(root.GetProperty("accessors").EnumerateArray());
    }

    /// <summary>정점 데이터가 실려 있어야 삼각형이 그려진다.</summary>
    [Fact]
    public void Glb_CarriesABinaryChunk()
    {
        var glb = FakeMeshProvider.MinimalGlb();
        var jsonLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(12, 4));
        var binHeader = 20 + jsonLength;

        Assert.True(glb.Length > binHeader + 8, "BIN 청크가 없습니다");

        Assert.Equal(0x004E4942u, BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(binHeader + 4, 4)));

        // 꼭짓점 셋 × float 셋
        var binLength = BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(binHeader, 4));
        Assert.Equal(36u, binLength);
    }

    /// <summary>
    /// 저장소 검증을 실제로 통과하는가 — 형식만 흉내내면 Fake 관통이 저장 단계에서 막힌다.
    /// </summary>
    [Fact]
    public async Task Glb_PassesArtifactStorageValidation()
    {
        var storage = new InMemoryMeshArtifactStorage();
        using var content = new MemoryStream(FakeMeshProvider.MinimalGlb());

        var stored = await storage.SaveAsync(
            Guid.NewGuid(), MeshArtifactKind.Glb, content, null, 128 * 1024 * 1024, default);

        Assert.Equal("model/gltf-binary", stored.ContentType);
        Assert.Equal(FakeMeshProvider.MinimalGlb().Length, stored.SizeBytes);
    }

    /// <summary>FBX 는 서명만 본다 — 뷰어가 열지 않으므로 내용까지 갖출 이유가 없다.</summary>
    [Fact]
    public void Fbx_CarriesTheBinarySignature()
    {
        var fbx = FakeMeshProvider.MinimalFbx();

        Assert.Equal("Kaydara FBX Binary  \0"u8.ToArray(), fbx[..21]);
    }

    /// <summary>공유 상수와 어긋나지 않는다 — 정의가 둘이면 한쪽만 고쳐도 통과한다.</summary>
    [Fact]
    public void TestHelper_UsesTheSameBytesAsTheProvider()
    {
        Assert.Equal(FakeMeshProvider.MinimalGlb(), FakeGlb.Bytes);
    }

    // ─── 설정 ───

    /// <summary>GLB 의 JSON 청크를 꺼낸다.</summary>
    private static string JsonChunkOf(byte[] glb)
    {
        var length = (int)BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(12, 4));

        return Encoding.UTF8.GetString(glb, 20, length);
    }
}
