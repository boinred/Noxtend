using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Noxtend.Domain.Mesh;

namespace Noxtend.Infrastructure.Persistence.Configurations;

/// <summary>
/// Design Ref: §5.1~5.2 · §5.5 — 외부 실행 추적표와 그 입력.
///
/// **작업과 별도 표인 이유는 저장 경계다** (§9.2). 공정 성공과 결과 연결은 작업 트랜잭션
/// 안에서 확정되지만, 업로드 token·외부 작업 ID·진행률은 외부 호출 사이사이에 저장돼야
/// 한다. 같은 트랜잭션에 묶으면 폴링 한 번마다 작업 전체가 잠긴다.
/// </summary>
public sealed class MeshRunConfiguration : IEntityTypeConfiguration<MeshRun>
{
    public void Configure(EntityTypeBuilder<MeshRun> builder)
    {
        builder.ToTable("MeshRuns");
        builder.HasKey(run => run.Id);
        builder.Property(run => run.Id).ValueGeneratedNever();

        builder.Property(run => run.JobId).IsRequired();
        builder.Property(run => run.TaskId).IsRequired();
        builder.Property(run => run.PartId).IsRequired();
        builder.Property(run => run.RunNumber).IsRequired();

        builder.Property(run => run.ProviderConfigId).IsRequired();
        builder.Property(run => run.Model).HasMaxLength(64).IsRequired();

        // 열거형은 문자열이다 — 값이 중간에 끼면 int 열은 기존 행을 조용히 다른 뜻으로
        // 바꾼다. 이 열거형은 상태가 열셋이라 그 위험이 특히 크다
        builder.Property(run => run.Status).HasConversion<string>().HasMaxLength(32).IsRequired();

        builder.Property(run => run.ProviderTaskId).HasMaxLength(128);
        builder.Property(run => run.Progress).IsRequired();
        builder.Property(run => run.ModelSeed).IsRequired();
        builder.Property(run => run.TextureSeed).IsRequired();
        builder.Property(run => run.CreditsConsumed);

        builder.Property(run => run.LastProviderCode);
        builder.Property(run => run.LastProviderRequestId).HasMaxLength(128);
        builder.Property(run => run.FailureCode).HasMaxLength(64);

        builder.Property(run => run.StartedAt).IsRequired();
        builder.Property(run => run.UpdatedAt).IsRequired();
        builder.Property(run => run.SubmittedAt);
        builder.Property(run => run.CompletedAt);

        // checkpoint 는 폴링 루프와 사용자 재시도가 같은 행에 쓸 수 있다.
        // 나중 쓰기가 앞의 것을 모르고 덮으면 저장된 token 이나 작업 ID 가 사라진다
        // **그림자 속성으로 두면 안 된다.** checkpoint 는 매번 새 컨텍스트에서 저장되어
        // 인스턴스가 컨텍스트를 옮겨 다니는데, 그림자 값은 추적기에만 살아 있어 다음
        // 컨텍스트가 기본값으로 비교한다 — 정상 저장이 전부 충돌로 오인된다
        builder.Property(run => run.RowVersion).IsRowVersion();

        // 공정 하나의 몇 번째 실행인가 — 수동 재시도가 번호를 늘린다
        builder.HasIndex(run => new { run.TaskId, run.RunNumber }).IsUnique();

        // **외부 작업 ID 는 전역에서 하나뿐이어야 한다.** 같은 ID 가 둘이면 유료 작업 하나가
        // 실행 둘에 붙어 어느 쪽 결과인지 알 수 없다
        builder.HasIndex(run => run.ProviderTaskId)
            .IsUnique()
            .HasFilter("[ProviderTaskId] IS NOT NULL");

        // 파츠별 최신 실행 조회
        builder.HasIndex(run => new { run.JobId, run.PartId, run.RunNumber })
            .IsDescending(false, false, true);

        builder.ToTable(table => table.HasCheckConstraint(
            "CK_MeshRuns_Progress", "[Progress] BETWEEN 0 AND 100"));

        ConfigureInputs(builder);
        ConfigureArtifacts(builder);
    }

    /// <summary>
    /// 산출물도 소유 컬렉션이다 (§3.2 · D-08).
    ///
    /// **키가 <c>(실행, 종류)</c> 라 같은 종류가 두 번 들어갈 수 없다.** 저장소가 실행 ID 로
    /// 결정된 Blob 키를 쓰므로 재시도는 같은 자리를 덮어쓴다 — 행이 둘이면 그 사실과
    /// 어긋나고, 어느 쪽이 살아 있는 Blob 인지 알 수 없게 된다.
    /// </summary>
    private static void ConfigureArtifacts(EntityTypeBuilder<MeshRun> builder)
    {
        builder.OwnsMany(run => run.Artifacts, artifact =>
        {
            artifact.ToTable("MeshRunArtifacts");
            artifact.WithOwner().HasForeignKey("MeshRunId");

            artifact.HasKey("MeshRunId", nameof(MeshArtifact.Kind));
            artifact.Property(a => a.Kind).HasConversion<string>().HasMaxLength(16);

            artifact.Property(a => a.BlobKey).HasMaxLength(256).IsRequired();
            artifact.Property(a => a.ContentType).HasMaxLength(64).IsRequired();
            artifact.Property(a => a.SizeBytes).IsRequired();
            artifact.Property(a => a.CreatedAt).IsRequired();
        });

        // 재개한 워커가 이미 저장한 산출물을 알아야 한다 — 입력과 같은 이유다
        builder.Navigation(run => run.Artifacts).AutoInclude();
    }

    /// <summary>
    /// 입력은 소유 컬렉션이다 — 실행 없이 존재할 수 없고 항상 실행을 통해 읽힌다.
    /// </summary>
    private static void ConfigureInputs(EntityTypeBuilder<MeshRun> builder)
    {
        builder.OwnsMany(run => run.Inputs, input =>
        {
            input.ToTable("MeshRunInputs");
            input.WithOwner().HasForeignKey(i => i.MeshRunId);

            // 방향이 곧 키의 절반이다. 실행 하나에 방향은 넷뿐이고 같은 방향이 둘일 수 없다
            input.HasKey(i => new { i.MeshRunId, i.ViewDirection });
            input.Property(i => i.ViewDirection).HasConversion<string>().HasMaxLength(16);

            input.Property(i => i.GeneratedImageId).IsRequired();
            input.Property(i => i.SourceContentType).HasMaxLength(64);
            input.Property(i => i.UploadContentType).HasMaxLength(64);
            // 내구적 핸들일 때만 채워진다 — Meshy 의 base64 는 여기 오지 않는다 (D-10)
            input.Property(i => i.ProviderFileToken).HasMaxLength(256);
            input.Property(i => i.PreparedAt);
        });

        // 재개한 워커가 빈 방향을 찾으려면 입력이 늘 함께 와야 한다
        builder.Navigation(run => run.Inputs).AutoInclude();
    }
}
