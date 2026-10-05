using Noxtend.Domain.Job;

namespace Noxtend.Tests.Domain;

/// <summary>테스트용 장면 명세. 유효한 값 한 벌을 한 곳에 둔다.</summary>
public static class TestScene
{
    public static SceneSpec Default { get; } = new(
        // 이름과 색이 나뉜 신규 계약 — 색만 있던 시절의 fixture 는 화면이 이름 칸을 못 채운다
        [
            new PaletteEntry("청회색 바다", "#2E5C6E"),
            new PaletteEntry("주황색 목재", "#D97A3C"),
            new PaletteEntry("옅은 안개", "#F2E8DC"),
        ],
        TimeOfDay: "해질녘",
        Mood: "고요함",
        RenderingStyle: "수채 반사실",
        MaterialFeel: "거친 목재",
        new CameraSpec("one-point", "지면에서 1.6m", 0.55),
        new LightSpec("좌측 후방 15° 고도", "따뜻함", "부드러움"),
        new ScaleReference("부두 기둥", "높이 3m", HeightMeters: 3));
}
