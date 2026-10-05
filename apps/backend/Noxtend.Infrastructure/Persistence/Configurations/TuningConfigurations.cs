using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Noxtend.Tuning.Domain.Call;
using Noxtend.Tuning.Domain.Golden;
using Noxtend.Tuning.Domain.Prompt;

namespace Noxtend.Infrastructure.Persistence.Configurations;

/// <summary>
/// Design Ref: §3.5 — 프롬프트 버전.
///
/// **활성 버전이 단계마다 하나임을 DB 가 강제한다.** 필터 유니크 인덱스가 그 장치다.
/// 애플리케이션 규칙으로만 두면 동시 활성화 요청 둘이 겹칠 때 둘 다 켜지고,
/// 그 뒤로는 어느 쪽이 쓰이는지 알 수 없게 된다.
/// </summary>
public sealed class PromptVersionConfiguration : IEntityTypeConfiguration<PromptVersion>
{
    public void Configure(EntityTypeBuilder<PromptVersion> builder)
    {
        builder.ToTable("PromptVersions");
        builder.HasKey(p => p.Id);

        // #4 에서 배운 것: Guid PK 에 이것이 없으면 EF 가 새 행을 UPDATE 로 낸다
        builder.Property(p => p.Id).ValueGeneratedNever();

        builder.Property(p => p.Kind).HasConversion<string>().HasMaxLength(32).IsRequired();

        // 카테고리 — null 이 기본이므로 IsRequired() 를 붙이지 않는다 (Design §5.1)
        builder.Property(p => p.Category).HasConversion<string>().HasMaxLength(32);

        builder.Property(p => p.Version).IsRequired();

        // 프롬프트 본문에 상한을 두지 않는다. 길어지는 것이 정상이고,
        // 잘리면 조용히 깨진 프롬프트가 공급자에게 나간다
        builder.Property(p => p.System).IsRequired();
        builder.Property(p => p.User).IsRequired();
        builder.Property(p => p.JsonSchema).IsRequired();

        builder.Property(p => p.Note).HasMaxLength(500);
        builder.Property(p => p.IsActive).IsRequired();
        builder.Property(p => p.CreatedAt).IsRequired();

        // 버전 채번은 (Kind, Category) 스코프 — 카테고리마다 1부터 (Design §4.3).
        // 이름을 명시한다: 안 주면 EF 가 IX_PromptVersions_Kind_Category_Version 로 새로 짓는데
        // 옛 이름 IX_PromptVersions_Kind_Version 이 마이그레이션 테스트 주석에 인용돼 있다
        // HasFilter(null) 로 EF 의 자동 필터를 끈다. EF 는 nullable 컬럼이 낀 유니크
        // 인덱스에 [Category] IS NOT NULL 을 붙이는데, 그러면 기본(null) 행끼리 버전
        // 유일성이 풀려 (Extract, NULL, 1) 이 둘 생길 수 있다. SQL Server 는 NULL 을 같은
        // 값으로 봐 (Kind, NULL, Version) 유일성을 그대로 지키므로 필터가 필요 없다
        builder.HasIndex(p => new { p.Kind, p.Category, p.Version })
            .IsUnique()
            .HasFilter(null)
            .HasDatabaseName("IX_PromptVersions_Kind_Version");

        // 단계·카테고리마다 활성은 하나 — 필터 유니크 (Design §4.2).
        // SQL Server 유니크 인덱스는 NULL 들을 같은 값으로 취급하므로 (Kind, NULL) 활성도
        // 하나로 강제된다 — 기본 프롬프트의 유일성이 별도 장치 없이 함께 보장된다
        builder.HasIndex(p => new { p.Kind, p.Category })
            .IsUnique()
            .HasFilter("[IsActive] = 1")
            .HasDatabaseName("IX_PromptVersions_ActiveByKindCategory");
    }
}

/// <summary>모델 단가 — 모델당 시행일별로 한 행.</summary>
public sealed class ModelPriceConfiguration : IEntityTypeConfiguration<ModelPrice>
{
    public void Configure(EntityTypeBuilder<ModelPrice> builder)
    {
        builder.ToTable("ModelPrices");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();

        builder.Property(p => p.Model).HasMaxLength(128).IsRequired();

        // decimal(18,6) — 100만 토큰당 USD. gpt-5-nano 의 $0.05 까지 담고도 여유가 있다
        builder.Property(p => p.InputPerMillion).HasPrecision(18, 6).IsRequired();
        builder.Property(p => p.OutputPerMillion).HasPrecision(18, 6).IsRequired();
        builder.Property(p => p.LongInputPerMillion).HasPrecision(18, 6);
        builder.Property(p => p.LongOutputPerMillion).HasPrecision(18, 6);

        // 장당 USD (사이클 #7 · Plan D-8). 토큰 과금 모델이면 null 이다 —
        // 토큰 칸에 환산해 넣으면 단가표를 읽는 사람이 그 값을 토큰 단가로 오해한다
        builder.Property(p => p.PerImage).HasPrecision(18, 6);

        builder.Property(p => p.EffectiveFrom).IsRequired();
        builder.Property(p => p.Note).HasMaxLength(500);

        // 같은 모델·같은 시행일이 둘이면 어느 쪽이 이길지 알 수 없다.
        // 화면에서도 막지만, 두 요청이 겹치면 화면 검사만으로는 새는 자리다
        builder
            .HasIndex(p => new { p.Model, p.EffectiveFrom })
            .IsUnique()
            .HasDatabaseName("IX_ModelPrices_ModelEffectiveFrom");
    }
}

/// <summary>Design Ref: §3.5 — LLM 호출 내역.</summary>
public sealed class LlmCallConfiguration : IEntityTypeConfiguration<LlmCall>
{
    public void Configure(EntityTypeBuilder<LlmCall> builder)
    {
        builder.ToTable("LlmCalls");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();

        builder.Property(c => c.JobId).IsRequired();
        builder.Property(c => c.TaskId);
        builder.Property(c => c.SimilarityEvaluationId);
        builder.Property(c => c.Kind).HasConversion<string>().HasMaxLength(32).IsRequired();

        // 상관관계는 공정 또는 유사도 평가 정확히 하나 (§7.3) — 애플리케이션은 factory 로
        // 막지만 마지막 방어선은 DB 다
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_LlmCalls_ExactlyOneCorrelation",
            "([TaskId] IS NULL AND [SimilarityEvaluationId] IS NOT NULL) OR " +
            "([TaskId] IS NOT NULL AND [SimilarityEvaluationId] IS NULL)"));

        // non-nullable 이 계약이다 (§3.2). 없으면 재현이 불가능하다
        builder.Property(c => c.PromptVersionId).IsRequired();

        builder.Property(c => c.ProviderConfigId).IsRequired();
        builder.Property(c => c.Model).HasMaxLength(128).IsRequired();

        // 요청·응답 전문. 상한 없음 — 자르면 진단 가치가 사라진다
        builder.Property(c => c.RequestPayload).IsRequired();
        builder.Property(c => c.ResponsePayload);

        builder.Property(c => c.FailureReason).HasMaxLength(500);

        // 장당 과금의 곱수 (사이클 #7). 텍스트 호출이면 null 이다
        builder.Property(c => c.OutputImages);

        builder.Property(c => c.At).IsRequired();

        builder.HasIndex(c => c.JobId);
        builder.HasIndex(c => c.TaskId);
        builder.HasIndex(c => new { c.Kind, c.At });
    }
}

/// <summary>Design Ref: §3.5 — 골든 샘플.</summary>
public sealed class GoldenSampleConfiguration : IEntityTypeConfiguration<GoldenSample>
{
    public void Configure(EntityTypeBuilder<GoldenSample> builder)
    {
        builder.ToTable("GoldenSamples");
        builder.HasKey(g => g.Id);
        builder.Property(g => g.Id).ValueGeneratedNever();

        builder.Property(g => g.StoredImageId).IsRequired();
        builder.Property(g => g.Name).HasMaxLength(128).IsRequired();
        builder.Property(g => g.ExpectedNote).HasMaxLength(2000).IsRequired();
        builder.Property(g => g.CreatedAt).IsRequired();

        // 같은 이미지를 두 번 등록할 이유가 없다
        builder.HasIndex(g => g.StoredImageId).IsUnique();
    }
}

/// <summary>Design Ref: §3.5 — 사람의 판정. 작업 하나에 하나.</summary>
public sealed class VerdictConfiguration : IEntityTypeConfiguration<Verdict>
{
    public void Configure(EntityTypeBuilder<Verdict> builder)
    {
        builder.ToTable("Verdicts");
        builder.HasKey(v => v.Id);
        builder.Property(v => v.Id).ValueGeneratedNever();

        builder.Property(v => v.JobId).IsRequired();
        builder.Property(v => v.IsPass).IsRequired();
        builder.Property(v => v.Memo).HasMaxLength(2000).IsRequired();
        builder.Property(v => v.At).IsRequired();

        // 작업 하나에 판정 하나 — 재판정은 덮어쓰기다
        builder.HasIndex(v => v.JobId).IsUnique();
    }
}
