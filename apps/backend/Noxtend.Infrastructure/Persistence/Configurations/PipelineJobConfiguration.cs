using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Noxtend.Domain.Job;
using Noxtend.Domain.Mesh;
using Noxtend.Domain.Sprites;
using Noxtend.Infrastructure.Persistence.Serialization;

namespace Noxtend.Infrastructure.Persistence.Configurations;

/// <summary>Design Ref: §3.3 — Jobs table plus its two owned collections.</summary>
public sealed class PipelineJobConfiguration : IEntityTypeConfiguration<PipelineJob>
{
    public void Configure(EntityTypeBuilder<PipelineJob> builder)
    {
        builder.ToTable("Jobs");
        builder.HasKey(j => j.Id);

        builder.Property(j => j.ProductionMode).HasConversion<string>().HasMaxLength(16)
            .HasDefaultValue(ProductionMode.ThreeD);

        // Identity comes from the domain, never from the database.
        //
        // Without ValueGeneratedNever, EF's convention marks a Guid key as generated-on-add
        // and then infers state from whether the key is populated. A domain-created entity
        // added to an already-tracked parent is read as "existing" and emitted as UPDATE
        // against a row that was never inserted — which surfaces as a concurrency exception,
        // not as the mis-detected insert it actually is.
        builder.Property(j => j.Id).ValueGeneratedNever();

        // Enums persist as strings. An int column would silently remap every existing
        // row the day a value is inserted in the middle of the enum — and TaskKind is
        // expected to grow (§2.3)
        builder.Property(j => j.Category).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(j => j.Status).HasConversion<string>().HasMaxLength(32).IsRequired();

        builder.Property(j => j.SourceImageId).IsRequired();
        // 장면 명세는 JSON 열이다 (§2.3-1). 필드가 R-1 실측 후 바뀔 가능성이 높은데
        // 열로 쪼개면 그때마다 마이그레이션이 필요하고, 장면으로 검색할 일은 없다
        builder.Property(j => j.Scene)
            .HasColumnName("SceneJson")
            .HasConversion(
                scene => scene == null ? null : SceneJsonSerializer.Serialize(scene),
                json => SceneJsonSerializer.Deserialize(json));
        // 파츠를 그릴 공급자·모델 (사이클 #7). 접수 시점에 정해지고 분해가 끝난 뒤에 쓰인다.
        // 이미지 생성 없이 접수된 옛 작업은 둘 다 null 이다
        builder.Property(j => j.ImageProviderConfigId);
        builder.Property(j => j.ImageModel).HasMaxLength(128);

        // 파츠를 3D 로 만들 공급자·모델 (사이클 #10 §4.1). 고르지 않은 작업은 둘 다 null 이고
        // 이미지까지만 돈다 — 그래서 마이그레이션이 기존 행을 건드리지 않아도 된다 (NFR-06)
        builder.Property(j => j.MeshProviderConfigId);
        builder.Property(j => j.MeshModel).HasMaxLength(128);

        // 캐릭터 고유 입력 (character-studio §D-01·§D-02). 둘 다 nullable — 타 카테고리는 안 보낸다.
        // 성별은 enum 을 문자열로(정수 열은 값 추가 때 옛 행을 조용히 재매핑). 힌트는 백본이
        // 해석하지 않는 JSON 문자열이라 owned 엔티티 대신 한 컬럼에 굳힌다
        builder.Property(j => j.Gender).HasConversion<string>().HasMaxLength(16);
        builder.Property(j => j.PartHints);

        // 검수 게이트 opt-in (review-gate 결정로그 D-01). 기본값 false 라 기존 행은
        // 마이그레이션 없이도 "검수 없이 접수됨"으로 그대로 읽힌다
        builder.Property(j => j.RequiresReview).IsRequired();
        builder.Property(j => j.ReviewPhase).HasConversion<string>().HasMaxLength(32).IsRequired();
        // 파츠만 바뀌는 검수 편집의 동시성 검사용 — PipelineJob.ReviewRevision
        builder.Property(j => j.ReviewRevision).IsRequired();

        // 서술 재작성 대상 (occludedby-recompute §구현 범위 1). 파츠 추가와 승인이 별개
        // 요청이라 그 사이 DB 에 남아야 한다 — 재작성 공정이 대상을 아는 유일한 통로다.
        // 백킹 필드를 매핑한다: 공개 프로퍼티는 읽기 전용 뷰다
        builder.Property<List<string>>("_descriptionsStale")
            .HasColumnName("DescriptionsStale");

        // 검수 편집과 승인이 겹치면 파츠가 Generate 공정 없이 남는다 (occludedby-recompute
        // §구현 범위 5). 실 DB 로 재현했다 — ReviewConcurrencyTests
        builder.Property(j => j.RowVersion).IsRowVersion();

        builder.Property(j => j.FailureReason).HasMaxLength(256);
        builder.Property(j => j.CreatedAt).IsRequired();
        builder.Property(j => j.CompletedAt);

        // 홈 "최근 작업"
        builder.HasIndex(j => j.CreatedAt).IsDescending();
        // 카테고리별 목록 — 스튜디오 3종 대비 (§2.4)
        builder.HasIndex(j => new { j.Category, j.CreatedAt }).IsDescending(false, true);
        // 홈 "실행 중"
        builder.HasIndex(j => new { j.Status, j.CreatedAt });

        ConfigureTasks(builder);
        ConfigureParts(builder);
        ConfigureGeneratedImages(builder);
        ConfigureGeneratedMeshes(builder);
    }

    /// <summary>
    /// 공정은 소유 컬렉션이다. 작업 없이 존재할 수 없고 항상 작업을 통해 읽힌다 —
    /// 애그리게이트 경계가 곧 로딩 경계다.
    /// </summary>
    private static void ConfigureTasks(EntityTypeBuilder<PipelineJob> builder)
    {
        builder.OwnsMany(j => j.Tasks, task =>
        {
            task.Property(t => t.SpriteInput).HasColumnName("SpriteInputJson")
                .HasConversion(value => value == null ? null : SpriteJsonSerializer.Serialize(value),
                    json => json == null ? null : SpriteJsonSerializer.Deserialize<SpriteFrameInput>(json));
            task.Property(t => t.SpriteExportInput).HasColumnName("SpriteExportInputJson")
                .HasConversion(value => value == null ? null : SpriteJsonSerializer.Serialize(value),
                    json => json == null ? null : SpriteJsonSerializer.Deserialize<SpriteExportInput>(json));
            task.Property(t => t.RequestId);
            task.ToTable("Tasks");
            task.WithOwner().HasForeignKey(t => t.JobId);
            task.HasKey(t => t.Id);
            task.Property(t => t.Id).ValueGeneratedNever();

            // 워커 둘이 같은 공정을 들면 나중 쓰기가 앞의 것을 모른 채 덮는다 —
            // 실제로 성공과 실패가 한 행에 섞여 남았다
            task.Property(t => t.RowVersion).IsRowVersion();

            task.Property(t => t.Kind).HasConversion<string>().HasMaxLength(32).IsRequired();
            task.Property(t => t.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
            task.Property(t => t.Ordinal).IsRequired();
            task.Property(t => t.DependsOnTaskId);
            task.Property(t => t.ProviderConfigId);

            // 공정마다 다른 모델을 쓸 수 있다. ProviderConfigId 와 짝이라 함께 null 이 된다 —
            // LLM 을 부르지 않는 미래의 단계를 위해 nullable 이다
            task.Property(t => t.Model).HasMaxLength(128);

            // 생성 공정만 값을 갖는다 (사이클 #7). 파츠별 진행 표시와 재시도가 이 열로 파츠를 찾는다
            task.Property(t => t.PartId);
            task.Property(t => t.ViewDirection).HasConversion<string>().HasMaxLength(16);
            task.HasIndex(t => t.PartId);

            // 3D 재구성이 얼린 네 방향 이미지 (§4.3). 장면 명세와 같은 이유로 JSON 한 열이다 —
            // 네 ID 로 검색하거나 정렬할 일이 없고, 범용 입력 표를 미리 만들지 않으면서도
            // 공정 재현성을 보존한다
            task.Property(t => t.MeshInputs)
                .HasColumnName("MeshInputsJson")
                .HasConversion(
                    inputs => inputs == null ? null : MeshInputSetJsonSerializer.Serialize(inputs),
                    json => MeshInputSetJsonSerializer.Deserialize(json));

            // 파츠당 3D 공정 하나 (§4.4 멱등 2층). 도메인 검사는 같은 aggregate 안에서만
            // 유효해서, 워커 둘이 각자 읽은 사본으로 동시에 계획하면 통과한다
            task.HasIndex(t => new { t.PartId, t.Kind })
                .IsUnique()
                .HasFilter($"[{nameof(PipelineTask.Kind)}] = '{nameof(TaskKind.Reconstruct)}'");

            task.Property(t => t.AttemptCount).IsRequired();
            task.Property(t => t.LeaseExpiresAt);
            // 재시도 백오프 대기 시각 (generation-rate-limiting §3②)
            task.Property(t => t.NotBefore);
            task.Property(t => t.FailureReason).HasMaxLength(256);
            task.Property(t => t.StartedAt);
            task.Property(t => t.CompletedAt);

            // 스위퍼 조회
            task.HasIndex(t => new { t.Status, t.Kind });
            // 리스 만료 감지 — 이 인덱스가 Redis 장애를 지연으로 격하시키는 조회를 받친다 (§3.3)
            task.HasIndex(t => new { t.Status, t.LeaseExpiresAt });
        });

        builder.Navigation(j => j.Tasks).AutoInclude();
    }

    private static void ConfigureParts(EntityTypeBuilder<PipelineJob> builder)
    {
        builder.OwnsMany(j => j.Parts, part =>
        {
            part.ToTable("AssetParts");
            part.WithOwner().HasForeignKey(p => p.JobId);
            part.HasKey(p => p.Id);

            // 파츠는 이미 추적 중인 작업에 나중에 붙는다 (ApplyParts). 다시 분석하면
            // 통째로 교체되므로, 여기가 위 주석의 오검출이 실제로 터지는 지점이다
            part.Property(p => p.Id).ValueGeneratedNever();

            part.Property(p => p.Name).HasMaxLength(128).IsRequired();
            part.Property(p => p.Ordinal).IsRequired();

            // 분해가 채운다 (사이클 #5). 그 전에는 전부 null 이다
            part.Property(p => p.Description);
            // 서술 출처 — 기존 enum 컬럼 관례대로 문자열 저장
            part.Property(p => p.DescriptionSource).HasConversion<string>().HasMaxLength(16).IsRequired();
            part.Property(p => p.Category).HasMaxLength(64);
            part.Property(p => p.DepthOrder);

            // 배치는 개수가 변하는 값의 목록이라 열로 펼 수 없다 — 행이 맞다 (D-02).
            //
            // **`Ordinal` 이 없으면 순서가 조용히 무너진다.** 테이블은 행 순서를 보장하지
            // 않는다. 정렬 없이 읽으면 대개 넣은 순서로 나오다가 어느 날 뒤섞이고, 그때
            // 모델이 낸 순서(FR-09)가 깨진 것을 아무도 눈치채지 못한다
            // `Placements` 는 정렬해서 내는 계산 값이다 — EF 가 내비게이션으로 오해하지
            // 않도록 명시적으로 뺀다
            part.Ignore(p => p.Placements);

            // 공개 프로퍼티가 아니라 백킹 필드를 매핑한다 — `Placements` 는 정렬해서 내는
            // 계산 값이라 EF 가 쓸 수 없다
            part.OwnsMany<PartPlacement>("_placements", placement =>
            {
                placement.ToTable("PartPlacements");
                placement.WithOwner().HasForeignKey("AssetPartId");
                // DB 가 번호를 매기면 도메인이 정한 순서가 아니게 된다
                placement.Property(p => p.Ordinal).ValueGeneratedNever();
                placement.HasKey("AssetPartId", nameof(PartPlacement.Ordinal));
                placement.OwnsOne(p => p.Bounds, bounds =>
                {
                    bounds.Property(b => b.X).HasColumnName("X");
                    bounds.Property(b => b.Y).HasColumnName("Y");
                    bounds.Property(b => b.W).HasColumnName("W");
                    bounds.Property(b => b.H).HasColumnName("H");
                });
            });

            // 문자열 목록 — EF Core 의 primitive collection 이 JSON 열로 매핑한다.
            // 구분자 join 은 파츠 이름에 구분자가 들어가면 깨진다 (§2.3-3)
            part.PrimitiveCollection(p => p.OccludedBy);

            // 생성이 채운다 (사이클 #7). 최신 이미지만 가리키고 이력은 GeneratedImages 에 쌓인다
            part.Property(p => p.GeneratedImageId);

            // review-gate — 검수 화면에서 사람이 추가한 파츠 표식. 기본값 false 라
            // 기존 행(전부 VLM 감지)은 마이그레이션 없이도 그대로 읽힌다
            part.Property(p => p.IsManuallyAdded).IsRequired();
        });

        builder.Navigation(j => j.Parts).AutoInclude();
    }

    /// <summary>
    /// 생성 이미지도 소유 컬렉션이다 — 작업 없이 존재할 수 없다.
    ///
    /// **파츠에 owned 로 넣지 않은 이유**는 재생성하면 행이 쌓이는데 파츠는 하나뿐이어서다.
    /// 파츠 밑에 두면 "최신 하나" 와 "이력 전부" 를 같은 자리에서 표현하게 된다 (§3.1).
    ///
    /// 바이트는 여기 없다 — <c>BlobKey</c> 만 있다 (NFR-10).
    /// </summary>
    private static void ConfigureGeneratedImages(EntityTypeBuilder<PipelineJob> builder)
    {
        builder.OwnsMany(j => j.GeneratedImages, image =>
        {
            image.ToTable("GeneratedImages");
            image.WithOwner().HasForeignKey(i => i.JobId);
            image.HasKey(i => i.Id);
            image.Property(i => i.Id).ValueGeneratedNever();

            image.Property(i => i.PartId).IsRequired();
            image.Property(i => i.TaskId).IsRequired();
            image.Property(i => i.ViewDirection).HasConversion<string>().HasMaxLength(16).IsRequired();
            image.Property(i => i.BlobKey).HasMaxLength(512).IsRequired();
            image.Property(i => i.ContentType).HasMaxLength(64).IsRequired();
            image.Property(i => i.SizeBytes).IsRequired();
            image.Property(i => i.CreatedAt).IsRequired();
            image.Property(i => i.IsObsoleted).IsRequired();
            image.Property(i => i.IsSynthetic).IsRequired();
            // 계산 프로퍼티다 — 저장할 열이 아니다 (spec 20260917)
            image.Ignore(i => i.IsCurrentCandidate);

            // 파츠별 최신 이미지 조회 — 골든 비교가 파츠 이름으로 마주 보게 정렬한다
            image.HasIndex(i => i.PartId);
        });

        builder.Navigation(j => j.GeneratedImages).AutoInclude();
    }

    /// <summary>
    /// 3D 결과도 작업이 소유한다 (§4.5).
    ///
    /// 실행 이력은 별도 표(<c>MeshRuns</c>)이지만 산출물은 작업 안에 둔다 — 사용자가
    /// 내려받는 것이고, 작업 하나를 읽는 것으로 화면에 필요한 것이 다 나와야 한다.
    /// </summary>
    private static void ConfigureGeneratedMeshes(EntityTypeBuilder<PipelineJob> builder)
    {
        builder.OwnsMany(j => j.GeneratedMeshes, mesh =>
        {
            mesh.ToTable("GeneratedMeshes");
            mesh.WithOwner().HasForeignKey(m => m.JobId);
            mesh.HasKey(m => m.Id);
            mesh.Property(m => m.Id).ValueGeneratedNever();

            mesh.Property(m => m.PartId).IsRequired();
            mesh.Property(m => m.TaskId).IsRequired();
            mesh.Property(m => m.MeshRunId).IsRequired();

            mesh.Property(m => m.CreditsConsumed);
            mesh.Property(m => m.CreatedAt).IsRequired();

            // 파츠별 최신 결과 조회
            mesh.HasIndex(m => new { m.PartId, m.CreatedAt }).IsDescending(false, true);

            // 산출물이 열이 아니라 표다 (D-08) — GLB·FBX·미리보기가 같은 모양으로 산다
            mesh.OwnsMany(m => m.Artifacts, artifact =>
            {
                artifact.ToTable("GeneratedMeshArtifacts");
                artifact.WithOwner().HasForeignKey("GeneratedMeshId");

                artifact.HasKey("GeneratedMeshId", nameof(MeshArtifact.Kind));
                artifact.Property(a => a.Kind).HasConversion<string>().HasMaxLength(16);

                artifact.Property(a => a.BlobKey).HasMaxLength(512).IsRequired();
                artifact.Property(a => a.ContentType).HasMaxLength(64).IsRequired();
                artifact.Property(a => a.SizeBytes).IsRequired();
                artifact.Property(a => a.CreatedAt).IsRequired();

                // 빈 산출물이 성공으로 남으면 화면은 완료를 보여주고 내려받기는 0바이트를 준다
                artifact.ToTable(table => table.HasCheckConstraint(
                    "CK_GeneratedMeshArtifacts_Size", "[SizeBytes] > 0"));
            });

            mesh.Navigation(m => m.Artifacts).AutoInclude();
        });

        builder.Navigation(j => j.GeneratedMeshes).AutoInclude();
    }
}
