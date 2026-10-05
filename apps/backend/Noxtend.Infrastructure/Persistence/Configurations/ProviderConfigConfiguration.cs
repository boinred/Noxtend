using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Noxtend.Domain.Provider;

namespace Noxtend.Infrastructure.Persistence.Configurations;

/// <summary>Design Ref: §3.3 — ProviderConfigs. ApiKeyCipher 는 NVARCHAR(MAX).</summary>
public sealed class ProviderConfigConfiguration : IEntityTypeConfiguration<ProviderConfig>
{
    public void Configure(EntityTypeBuilder<ProviderConfig> builder)
    {
        builder.ToTable("ProviderConfigs");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();

        builder.Property(c => c.DisplayName).HasMaxLength(128).IsRequired();
        builder.Property(c => c.Kind).HasConversion<string>().HasMaxLength(32).IsRequired();

        // Data Protection 암호문은 길이가 키 링과 페이로드에 따라 달라진다. 상한을 두면
        // 키 회전 후 저장이 잘리는 형태로 깨진다 — 그때는 복호화 실패로만 드러난다
        builder.Property(c => c.ApiKeyCipher).IsRequired();

        builder.Property(c => c.ApiKeyLast4).HasMaxLength(4).IsRequired();
        builder.Property(c => c.IsEnabled).IsRequired();
        builder.Property(c => c.CreatedAt).IsRequired();
        builder.Property(c => c.UpdatedAt).IsRequired();

        // 계산 속성이다. 저장하면 ApiKeyLast4 와 어긋날 수 있는 두 번째 진실이 생긴다
        builder.Ignore(c => c.MaskedApiKey);
    }
}
