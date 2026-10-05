using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Noxtend.Domain.Upload;

namespace Noxtend.Infrastructure.Persistence.Configurations;

/// <summary>Design Ref: §3.3 — StoredImages.</summary>
public sealed class StoredImageConfiguration : IEntityTypeConfiguration<StoredImage>
{
    public void Configure(EntityTypeBuilder<StoredImage> builder)
    {
        builder.ToTable("StoredImages");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).ValueGeneratedNever();

        builder.Property(i => i.BlobKey).HasMaxLength(256).IsRequired();
        // 원본 파일명은 표시용이다. 길이만 제한하고 경로로 쓰지 않는다 (§7)
        builder.Property(i => i.OriginalName).HasMaxLength(512).IsRequired();
        builder.Property(i => i.ContentType).HasMaxLength(128).IsRequired();
        builder.Property(i => i.SizeBytes).IsRequired();
        builder.Property(i => i.CreatedAt).IsRequired();
    }
}
