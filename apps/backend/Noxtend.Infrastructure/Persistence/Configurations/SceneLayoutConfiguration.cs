using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Noxtend.Domain.Scene;
using Noxtend.Domain.Scene.Projection;

namespace Noxtend.Infrastructure.Persistence.Configurations;

/// <summary>
/// Design Ref: scene-assembly §3.3 · background-similarity-tuning §4.1
/// — 인스턴스는 part-placements 의 OwnsMany 패턴, revision 열은 similarity-tuning 의 불변 모델.
/// </summary>
public sealed class SceneLayoutConfiguration : IEntityTypeConfiguration<SceneLayout>
{
    // 새 열이라 구형 형식이 없다 — SceneJsonSerializer 같은 방어 계층 없이 그대로 직렬화한다
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public void Configure(EntityTypeBuilder<SceneLayout> builder)
    {
        builder.ToTable("SceneLayouts");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedNever();

        // revision 채번은 job 안에서 유일 — 복원도 새 번호를 받는다 (§4.3)
        builder.HasIndex(l => new { l.JobId, l.Revision }).IsUnique();

        // 활성은 job 당 하나 — 필터드 유니크가 활성 교대 경쟁의 마지막 방어선 (§4.1)
        builder.HasIndex(l => l.JobId)
            .IsUnique()
            .HasFilter("[State] = 0")
            .HasDatabaseName("IX_SceneLayouts_ActivePerJob");

        builder.Property(l => l.Revision).IsRequired();
        builder.Property(l => l.State).IsRequired();
        builder.Property(l => l.Origin).IsRequired();
        builder.Property(l => l.SourceMeshCount).IsRequired();
        builder.Property(l => l.ComposedAt).IsRequired();

        // SHA-256 hex 64자 고정
        builder.Property(l => l.SourceMeshSignature).HasMaxLength(64).IsRequired();

        // 상태 전이 경쟁 감지 — 값은 불변이라 전이만 지키면 된다
        builder.Property(l => l.RowVersion).IsRowVersion();

        // 수치 카메라·조명은 JSON 열 — 구조가 통째로 읽고 쓰는 값 객체다
        builder.Property(l => l.Camera)
            .HasColumnName("CameraJson")
            .IsRequired()
            .HasConversion(
                camera => JsonSerializer.Serialize(camera, JsonOptions),
                json => JsonSerializer.Deserialize<SceneCamera>(json, JsonOptions)!);
        builder.Property(l => l.Light)
            .HasColumnName("LightJson")
            .IsRequired()
            .HasConversion(
                light => JsonSerializer.Serialize(light, JsonOptions),
                json => JsonSerializer.Deserialize<SceneLightRig>(json, JsonOptions)!);

        // 합성 요약은 nullable — 사이클 #18 이전에 만들어진 행에는 없다
        builder.Property(l => l.Composition)
            .HasColumnName("CompositionJson")
            .HasConversion(
                composition => composition == null
                    ? null
                    : JsonSerializer.Serialize(composition, JsonOptions),
                json => json == null
                    ? null
                    : JsonSerializer.Deserialize<CompositionSummary>(json, JsonOptions));

        // 정렬해서 내는 계산 값 — EF 가 내비게이션으로 오해하지 않도록 명시적으로 뺀다
        builder.Ignore(l => l.Instances);

        builder.OwnsMany<SceneInstance>("_instances", instance =>
        {
            instance.ToTable("SceneInstances");
            instance.WithOwner().HasForeignKey("SceneLayoutId");
            // 도메인이 정한 배치 번호다 — DB 가 매기면 순서가 어긋난다
            instance.Property(i => i.Ordinal).ValueGeneratedNever();
            instance.HasKey("SceneLayoutId", nameof(SceneInstance.PartId), nameof(SceneInstance.Ordinal));

            // 축별 배율 (#20 §4.2) — 낱개는 세 값이 같고 표면만 갈린다.
            // Scale·IsUniform 은 계산 값이라 저장하지 않는다
            instance.Ignore(i => i.Scale);
            instance.Ignore(i => i.IsUniform);
        });
    }
}
