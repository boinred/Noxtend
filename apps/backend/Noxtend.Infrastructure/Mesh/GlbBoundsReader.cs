using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Noxtend.Domain.Scene;

namespace Noxtend.Infrastructure.Mesh;

/// <summary>
/// GLB 의 장면 그래프를 읽어 월드 바운딩 박스를 낸다.
///
/// Design Ref: background-placement-projection(#19) 후속 — 납작한 파츠 대응
///
/// **뷰어와 같은 값이어야 한다.** 뷰어는 `Box3.setFromObject` 로 노드 변환까지 적용한
/// 월드 박스를 재고 그 높이로 정규화한다. 서버가 accessor 의 `min`·`max` 만 보면
/// 회전·스케일이 붙은 노드에서 축이 뒤바뀌어 판정이 반대로 뒤집힌다.
///
/// 삼각형 데이터를 읽지 않는다 — accessor 가 이미 담고 있는 축별 최소·최대를 노드
/// 변환으로 옮겨 합칠 뿐이다. 파일 크기와 무관하게 JSON 청크만 훑는다.
/// </summary>
public static class GlbBoundsReader
{
    private const uint JsonChunk = 0x4E4F534A;
    private const int HeaderBytes = 12;
    private const int ChunkHeaderBytes = 8;

    /// <summary>읽지 못하면 <c>null</c> — 형태를 모르면 서 있는 것으로 다룬다.</summary>
    public static MeshExtents? Read(ReadOnlySpan<byte> glb)
    {
        if (glb.Length < HeaderBytes + ChunkHeaderBytes || !glb[..4].SequenceEqual("glTF"u8))
        {
            return null;
        }

        var jsonLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(glb[HeaderBytes..]);
        var chunkType = BinaryPrimitives.ReadUInt32LittleEndian(glb[(HeaderBytes + 4)..]);
        var start = HeaderBytes + ChunkHeaderBytes;

        if (chunkType != JsonChunk || jsonLength <= 0 || start + jsonLength > glb.Length)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(
                Encoding.UTF8.GetString(glb.Slice(start, jsonLength)).TrimEnd('\0', ' '));

            return Measure(document.RootElement);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static MeshExtents? Measure(JsonElement root)
    {
        if (!root.TryGetProperty("nodes", out var nodes)
            || !root.TryGetProperty("meshes", out var meshes)
            || !root.TryGetProperty("accessors", out var accessors))
        {
            return null;
        }

        double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
        var found = false;

        // 장면이 지목한 뿌리부터 훑는다. 없으면 노드 전부를 뿌리로 본다 —
        // 어느 쪽이든 메시가 달린 노드를 한 번씩 거친다
        foreach (var (node, transform) in Walk(root, nodes))
        {
            if (!node.TryGetProperty("mesh", out var meshIndex)
                || meshIndex.GetInt32() >= meshes.GetArrayLength())
            {
                continue;
            }

            foreach (var box in MeshBoxes(meshes[meshIndex.GetInt32()], accessors))
            {
                // 변환된 박스의 여덟 꼭짓점 — 회전이 붙으면 축 정렬이 깨지므로 전부 본다
                foreach (var (x, y, z) in Corners(box))
                {
                    var (wx, wy, wz) = Apply(transform, x, y, z);
                    minX = Math.Min(minX, wx); maxX = Math.Max(maxX, wx);
                    minY = Math.Min(minY, wy); maxY = Math.Max(maxY, wy);
                    minZ = Math.Min(minZ, wz); maxZ = Math.Max(maxZ, wz);
                    found = true;
                }
            }
        }

        return found
            ? new MeshExtents(maxX - minX, maxY - minY, maxZ - minZ)
            : null;
    }

    /// <summary>노드 트리를 훑으며 누적 변환을 함께 낸다.</summary>
    private static IEnumerable<(JsonElement Node, double[] Transform)> Walk(
        JsonElement root, JsonElement nodes)
    {
        var roots = SceneRoots(root, nodes);
        var stack = new Stack<(int Index, double[] Parent)>(
            roots.Select(index => (index, Identity())));
        var visited = new HashSet<int>();

        while (stack.Count > 0)
        {
            var (index, parent) = stack.Pop();
            if (index < 0 || index >= nodes.GetArrayLength() || !visited.Add(index))
            {
                continue;
            }

            var node = nodes[index];
            var transform = Multiply(parent, LocalTransform(node));

            yield return (node, transform);

            if (node.TryGetProperty("children", out var children))
            {
                foreach (var child in children.EnumerateArray())
                {
                    stack.Push((child.GetInt32(), transform));
                }
            }
        }
    }

    private static IEnumerable<int> SceneRoots(JsonElement root, JsonElement nodes)
    {
        if (root.TryGetProperty("scenes", out var scenes) && scenes.GetArrayLength() > 0)
        {
            var index = root.TryGetProperty("scene", out var active) ? active.GetInt32() : 0;
            if (index >= 0 && index < scenes.GetArrayLength()
                && scenes[index].TryGetProperty("nodes", out var roots))
            {
                return [.. roots.EnumerateArray().Select(node => node.GetInt32())];
            }
        }

        return Enumerable.Range(0, nodes.GetArrayLength());
    }

    /// <summary>accessor 의 min·max — POSITION 만 본다.</summary>
    private static IEnumerable<(double[] Min, double[] Max)> MeshBoxes(
        JsonElement mesh, JsonElement accessors)
    {
        if (!mesh.TryGetProperty("primitives", out var primitives))
        {
            yield break;
        }

        foreach (var primitive in primitives.EnumerateArray())
        {
            if (!primitive.TryGetProperty("attributes", out var attributes)
                || !attributes.TryGetProperty("POSITION", out var position))
            {
                continue;
            }

            var index = position.GetInt32();
            if (index < 0 || index >= accessors.GetArrayLength())
            {
                continue;
            }

            var accessor = accessors[index];
            if (!accessor.TryGetProperty("min", out var min)
                || !accessor.TryGetProperty("max", out var max)
                || min.GetArrayLength() < 3 || max.GetArrayLength() < 3)
            {
                continue;
            }

            yield return (Triple(min), Triple(max));
        }
    }

    private static double[] Triple(JsonElement array)
        => [array[0].GetDouble(), array[1].GetDouble(), array[2].GetDouble()];

    private static IEnumerable<(double X, double Y, double Z)> Corners(
        (double[] Min, double[] Max) box)
    {
        foreach (var x in new[] { box.Min[0], box.Max[0] })
        {
            foreach (var y in new[] { box.Min[1], box.Max[1] })
            {
                foreach (var z in new[] { box.Min[2], box.Max[2] })
                {
                    yield return (x, y, z);
                }
            }
        }
    }

    /// <summary>노드의 지역 변환 — `matrix` 가 있으면 그대로, 없으면 TRS 를 합친다.</summary>
    private static double[] LocalTransform(JsonElement node)
    {
        if (node.TryGetProperty("matrix", out var matrix) && matrix.GetArrayLength() == 16)
        {
            // glTF 는 열 우선(column-major)이다
            return [.. matrix.EnumerateArray().Select(value => value.GetDouble())];
        }

        var result = Identity();

        if (node.TryGetProperty("scale", out var scale) && scale.GetArrayLength() == 3)
        {
            for (var axis = 0; axis < 3; axis++)
            {
                result[(axis * 4) + axis] = scale[axis].GetDouble();
            }
        }

        if (node.TryGetProperty("rotation", out var rotation) && rotation.GetArrayLength() == 4)
        {
            result = Multiply(Quaternion(rotation), result);
        }

        if (node.TryGetProperty("translation", out var translation)
            && translation.GetArrayLength() == 3)
        {
            for (var axis = 0; axis < 3; axis++)
            {
                result[12 + axis] = translation[axis].GetDouble();
            }
        }

        return result;
    }

    private static double[] Quaternion(JsonElement q)
    {
        double x = q[0].GetDouble(), y = q[1].GetDouble(), z = q[2].GetDouble(), w = q[3].GetDouble();
        var m = Identity();

        m[0] = 1 - (2 * ((y * y) + (z * z)));
        m[1] = 2 * ((x * y) + (z * w));
        m[2] = 2 * ((x * z) - (y * w));
        m[4] = 2 * ((x * y) - (z * w));
        m[5] = 1 - (2 * ((x * x) + (z * z)));
        m[6] = 2 * ((y * z) + (x * w));
        m[8] = 2 * ((x * z) + (y * w));
        m[9] = 2 * ((y * z) - (x * w));
        m[10] = 1 - (2 * ((x * x) + (y * y)));

        return m;
    }

    private static double[] Identity() =>
        [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];

    /// <summary>열 우선 4×4 곱 — `parent × child`.</summary>
    private static double[] Multiply(double[] parent, double[] child)
    {
        var result = new double[16];

        for (var column = 0; column < 4; column++)
        {
            for (var row = 0; row < 4; row++)
            {
                double sum = 0;
                for (var k = 0; k < 4; k++)
                {
                    sum += parent[(k * 4) + row] * child[(column * 4) + k];
                }

                result[(column * 4) + row] = sum;
            }
        }

        return result;
    }

    private static (double X, double Y, double Z) Apply(
        double[] m, double x, double y, double z)
        => ((m[0] * x) + (m[4] * y) + (m[8] * z) + m[12],
            (m[1] * x) + (m[5] * y) + (m[9] * z) + m[13],
            (m[2] * x) + (m[6] * y) + (m[10] * z) + m[14]);
}
