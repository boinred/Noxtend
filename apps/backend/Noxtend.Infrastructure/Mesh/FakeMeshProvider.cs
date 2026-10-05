using System.Buffers.Binary;
using System.Text;
using Noxtend.Domain.Job;
using Noxtend.Domain.Mesh;
using Noxtend.Domain.Ports;

namespace Noxtend.Infrastructure.Mesh;

/// <summary>
/// 외부 호출 없는 3D 공급자.
///
/// Design Ref: §12.2 · Plan FR-16 · NFR-08
///
/// **`Llm:UseFake` 하나가 전부를 가른다.** 텍스트만 가짜이고 3D 만 실제로 과금되는 조합을
/// 만들지 않는다 — 로컬에서 파이프라인을 한 번 돌릴 때마다 돈이 나가면 아무도 돌리지 않는다.
///
/// **진짜 GLB 바이트를 낸다.** 검증을 통과해야 관통이 성립하므로, 형식을 흉내내는 것이
/// 아니라 실제로 열리는 최소 glTF 2.0 컨테이너를 만든다.
/// </summary>
/// <param name="producesFbx">
/// 공급자가 FBX 도 내는가. Meshy 만 낸다 (Plan D-05) — 가짜에서도 그 차이를 지켜야
/// 화면의 "FBX 는 있을 때만" 규칙을 E2E 가 실제로 검증한다.
/// </param>
public sealed class FakeMeshProvider(bool producesFbx = false) : IMeshProvider
{
    /// <summary>몇 번 조회하면 완료로 넘어가는가 — 진행 중 상태를 화면에서 볼 수 있게 한다.</summary>
    private const int PollsBeforeSuccess = 2;

    private int polls;

    public Task<MeshInputHandle> UploadInputAsync(MeshInputUpload input, CancellationToken ct)
        // 방향마다 결정된 값이라 재기동 후에도 같은 참조가 나온다
        => Task.FromResult(new MeshInputHandle(
            $"fake-file-{input.Direction.ToString().ToLowerInvariant()}", IsDurable: true));

    public Task<MeshSubmission> SubmitAsync(MultiviewMeshRequest request, CancellationToken ct)
        => Task.FromResult(new MeshSubmission($"fake-task-{Guid.NewGuid():n}"));

    public Task<MeshTaskSnapshot> GetTaskAsync(string providerTaskId, CancellationToken ct)
    {
        var count = Interlocked.Increment(ref polls);

        return Task.FromResult(count switch
        {
            1 => new MeshTaskSnapshot(MeshTaskState.Pending, 0, null, null, null, null),
            <= PollsBeforeSuccess => new MeshTaskSnapshot(MeshTaskState.Running, 50, null, null, null, null),
            _ => new MeshTaskSnapshot(MeshTaskState.Succeeded, 100, CreditsConsumed: 0, null, null, null),
        });
    }

    public Task<IMeshResultDownload> OpenResultAsync(string providerTaskId, CancellationToken ct)
        => Task.FromResult<IMeshResultDownload>(new FakeDownload(producesFbx));

    /// <summary>
    /// 삼각형 하나가 든 glTF 2.0 이진 컨테이너.
    ///
    /// **형식 검증만 통과하는 것으로는 부족하다.** 전에는 `asset` 만 담은 JSON 청크
    /// 하나였는데, 그것은 우리 저장소 검증(magic·버전·길이)을 통과하면서도 **뷰어에서는
    /// 열리지 않았다** — 뷰어가 "Model does not have a scene" 으로 거절한다. 장면만 넣고
    /// 비워 두면 이번에는 `loadfailure` 다. 둘 다 실측으로 확인했다.
    ///
    /// 그래서 실제로 그려지는 최소한을 담는다 — 장면 하나, 노드 하나, 삼각형 하나.
    /// 가짜 공급자의 결과도 화면에서 열려야 로컬에서 파이프라인 전체를 볼 수 있다.
    /// </summary>
    internal static byte[] MinimalGlb()
    {
        // 삼각형 세 꼭짓점 × float 셋
        var positions = new[] { 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f, 0f };
        var bin = new byte[positions.Length * sizeof(float)];

        for (var i = 0; i < positions.Length; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bin.AsSpan(i * sizeof(float)), positions[i]);
        }

        var json = Encoding.UTF8.GetBytes(
            """
            {"asset":{"version":"2.0"},"scene":0,"scenes":[{"nodes":[0]}],
             "nodes":[{"mesh":0}],
             "meshes":[{"primitives":[{"attributes":{"POSITION":0}}]}],
             "buffers":[{"byteLength":36}],
             "bufferViews":[{"buffer":0,"byteOffset":0,"byteLength":36,"target":34962}],
             "accessors":[{"bufferView":0,"componentType":5126,"count":3,"type":"VEC3",
                           "min":[0,0,0],"max":[1,1,0]}]}
            """);

        // 청크는 4바이트 경계에 맞아야 한다 — JSON 은 공백, BIN 은 0 으로 채운다
        var jsonChunk = Pad(json, (byte)' ');
        var binChunk = Pad(bin, 0);

        var total = 12 + 8 + jsonChunk.Length + 8 + binChunk.Length;
        var glb = new byte[total];

        "glTF"u8.CopyTo(glb);
        BinaryPrimitives.WriteUInt32LittleEndian(glb.AsSpan(4), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(glb.AsSpan(8), (uint)total);

        BinaryPrimitives.WriteUInt32LittleEndian(glb.AsSpan(12), (uint)jsonChunk.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(glb.AsSpan(16), 0x4E4F534A);   // 'JSON'
        jsonChunk.CopyTo(glb.AsSpan(20));

        var binHeader = 20 + jsonChunk.Length;
        BinaryPrimitives.WriteUInt32LittleEndian(glb.AsSpan(binHeader), (uint)binChunk.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(glb.AsSpan(binHeader + 4), 0x004E4942);   // 'BIN\0'
        binChunk.CopyTo(glb.AsSpan(binHeader + 8));

        return glb;
    }

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

    /// <summary>1×1 PNG — 미리보기 검증의 magic bytes 를 통과한다.</summary>
    internal static byte[] MinimalPng() =>
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
        0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41,
        0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
        0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00,
        0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
        0x42, 0x60, 0x82,
    ];

    /// <summary>
    /// 이진 FBX 의 최소 헤더.
    ///
    /// 서명 21바이트 + <c>0x1A 0x00</c> + 버전. 검증이 서명만 보므로 이 정도면 통과한다 —
    /// 실제로 열리는 장면을 만드는 것은 가짜 공급자의 일이 아니다.
    /// </summary>
    internal static byte[] MinimalFbx()
    {
        var header = new byte[27];

        "Kaydara FBX Binary  \0"u8.CopyTo(header);
        header[21] = 0x1A;
        header[22] = 0x00;
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(23), 7400);

        return header;
    }

    private sealed class FakeDownload : IMeshResultDownload
    {
        public FakeDownload(bool producesFbx)
        {
            var parts = new List<MeshResultPart>
            {
                new(MeshArtifactKind.Glb, new MemoryStream(MinimalGlb()), "model/gltf-binary"),
                new(MeshArtifactKind.Preview, new MemoryStream(MinimalPng()), "image/png"),
            };

            if (producesFbx)
            {
                parts.Add(new MeshResultPart(
                    MeshArtifactKind.Fbx, new MemoryStream(MinimalFbx()), "application/octet-stream"));
            }

            Parts = parts;
        }

        public IReadOnlyList<MeshResultPart> Parts { get; }

        public async ValueTask DisposeAsync()
        {
            foreach (var part in Parts)
            {
                await part.Content.DisposeAsync();
            }
        }
    }
}
