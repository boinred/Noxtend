using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Noxtend.Domain.Similarity;

namespace Noxtend.Infrastructure.Persistence.Configurations;

/// <summary>
/// Design Ref: background-similarity-tuning §5 · §13
///
/// **DB 가 마지막 방어선인 제약 둘**: job 별 비종료 run 하나(필터드 유니크),
/// (JobId, Idempotency-Key) 유니크. 애플리케이션 검사는 동시 요청 사이에서 진다.
/// </summary>
public sealed class SimilarityRunConfiguration : IEntityTypeConfiguration<SimilarityRun>
{
    public void Configure(EntityTypeBuilder<SimilarityRun> builder)
    {
        builder.ToTable("SimilarityRuns");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.JobId).IsRequired();
        builder.Property(r => r.Model).HasMaxLength(128).IsRequired();
        builder.Property(r => r.IdempotencyKey).HasMaxLength(128).IsRequired();
        builder.Property(r => r.FailureCode).HasMaxLength(64);

        // 상태 전이 경쟁(채택 vs 취소) 감지
        builder.Property(r => r.RowVersion).IsRowVersion();

        // 같은 시작 요청의 중복 접수 방어 (§10)
        builder.HasIndex(r => new { r.JobId, r.IdempotencyKey }).IsUnique();

        // 비종료 run 은 job 당 하나 (§5.1) — Evaluating·ReadyForAdjustment·AwaitingRender
        builder.HasIndex(r => r.JobId)
            .IsUnique()
            .HasFilter("[Status] IN (0, 1, 2)")
            .HasDatabaseName("IX_SimilarityRuns_OpenPerJob");
    }
}

public sealed class SimilarityEvaluationConfiguration : IEntityTypeConfiguration<SimilarityEvaluation>
{
    // 새 열이라 구형 형식이 없다 — 방어 계층 없이 그대로 직렬화한다 (SceneLayout 과 같은 규칙)
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public void Configure(EntityTypeBuilder<SimilarityEvaluation> builder)
    {
        builder.ToTable("SimilarityEvaluations");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.RunId).IsRequired();
        builder.Property(e => e.LayoutId).IsRequired();

        // run 안의 순번 — 기준 1, 후보 2부터. 겹치면 이력 화면이 성립하지 않는다
        builder.HasIndex(e => new { e.RunId, e.Sequence }).IsUnique();

        // run 삭제(작업 완전 삭제의 동반)와 함께 지운다
        builder.HasOne<SimilarityRun>()
            .WithMany()
            .HasForeignKey(e => e.RunId)
            .OnDelete(DeleteBehavior.Cascade);

        // 렌더 참조 — blob 은 저장소에, 여기는 정체만 (§5.2)
        builder.OwnsOne(e => e.Render, render =>
        {
            render.Property(r => r.BlobKey).HasColumnName("RenderBlobKey").HasMaxLength(256);
            render.Property(r => r.ContentType).HasColumnName("RenderContentType").HasMaxLength(64);
            render.Property(r => r.SizeBytes).HasColumnName("RenderSizeBytes");
            render.Property(r => r.Sha256).HasColumnName("RenderSha256").HasMaxLength(64);
        });

        // 점수는 여섯 축만 저장하고 overall 은 항상 다시 계산한다 (D-05) —
        // 저장된 overall 을 믿으면 가중치 변경 시 두 값이 어긋난다
        builder.Property(e => e.Score)
            .HasColumnName("ScoreJson")
            .HasConversion(
                score => score == null
                    ? null
                    : JsonSerializer.Serialize(score.Dimensions, JsonOptions),
                json => json == null
                    ? null
                    : SimilarityScore.Create(
                        JsonSerializer.Deserialize<List<SimilarityDimension>>(json, JsonOptions)!));

        // 보정 제안 — discriminated command 가 담기므로 다형 직렬화 (SceneAdjustmentCommand 의 type 판별자)
        builder.Property(e => e.Adjustments)
            .HasColumnName("AdjustmentsJson")
            .HasConversion(
                adjustments => JsonSerializer.Serialize(adjustments, JsonOptions),
                json => JsonSerializer.Deserialize<List<SimilarityAdjustment>>(json, JsonOptions)!
                    .AsReadOnly());

        builder.Property(e => e.RegenerationNotes)
            .HasColumnName("RegenerationNotesJson")
            .HasConversion(
                notes => JsonSerializer.Serialize(notes, JsonOptions),
                json => JsonSerializer.Deserialize<List<string>>(json, JsonOptions)!
                    .AsReadOnly());

        builder.Property(e => e.CreatedAt).IsRequired();
    }
}
