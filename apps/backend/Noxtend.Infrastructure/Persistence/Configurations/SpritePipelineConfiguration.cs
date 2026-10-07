using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Noxtend.Domain.Job;
using Noxtend.Domain.Sprites;

namespace Noxtend.Infrastructure.Persistence.Configurations;

public sealed class SpritePipelineConfiguration : IEntityTypeConfiguration<PipelineJob>
{
    public void Configure(EntityTypeBuilder<PipelineJob> builder)
    {
        builder.OwnsOne(job => job.Sprites, state =>
        {
            state.Property<byte[]>("RowVersion").IsRowVersion().HasColumnName("RowVersion");
            Json(state.Property(s => s.Settings), "SpriteSettingsJson");
            Json(state.Property(s => s.SourceCanvas), "SpriteSourceCanvasJson");
            Json(state.Property(s => s.GenerationCanvas), "SpriteGenerationCanvasJson");
            Json(state.Property(s => s.OutputCanvas), "SpriteOutputCanvasJson");
            Json(state.Property(s => s.Transform), "SpriteTransformJson");
            state.Property(s => s.Phase).HasColumnName("SpritePhase").HasConversion<string>().HasMaxLength(32);
            state.Property(s => s.ReviewRevision).HasColumnName("SpriteReviewRevision");
            state.Property(s => s.CompletedExportId).HasColumnName("SpriteCompletedExportId");
            state.OwnsMany(s => s.Assets, asset =>
            {
                asset.ToTable("SpriteAssets");
                asset.WithOwner().HasForeignKey("JobId");
                asset.HasKey(a => a.Id);
                asset.Property(a => a.Id).ValueGeneratedNever();
                Json(asset.Property(a => a.Plan), "PlanJson");
                Json(asset.Property(a => a.Anchor), "AnchorJson");
                Json(asset.Property(a => a.Approval), "ApprovalJson");
                asset.OwnsMany(a => a.Frames, frame =>
                {
                    frame.ToTable("SpriteFrames");
                    frame.WithOwner().HasForeignKey("AssetId");
                    frame.HasKey("AssetId", nameof(SpriteFrame.Index));
                    frame.Property(f => f.Index).ValueGeneratedNever();
                });
            });
            // 대상 삭제와 독립적인 이미지·패키지 이력
            state.OwnsMany(s => s.Images, image =>
            {
                image.ToTable("SpriteImages");
                image.WithOwner().HasForeignKey("JobId");
                image.HasKey(i => i.Id);
                image.Property(i => i.Id).ValueGeneratedNever();
                image.Property(i => i.BlobKey).HasMaxLength(512).IsRequired()
                    .HasConversion(key => SpriteJsonSerializer.ValidateBlobKey(key),
                        key => SpriteJsonSerializer.ValidateBlobKey(key));
                image.Property(i => i.ContentType).HasMaxLength(64).IsRequired();
            });
            state.OwnsMany(s => s.Exports, export =>
            {
                export.ToTable("SpriteExports");
                export.WithOwner().HasForeignKey("JobId");
                export.HasKey(e => e.Id);
                export.Property(e => e.Id).ValueGeneratedNever();
                Json(export.Property(e => e.Input), "InputJson");
                Json(export.Property(e => e.Manifest), "ManifestJson");
                export.Property(e => e.BlobKey).HasMaxLength(512).IsRequired()
                    .HasConversion(key => SpriteJsonSerializer.ValidateBlobKey(key),
                        key => SpriteJsonSerializer.ValidateBlobKey(key));
            });
            state.OwnsMany(s => s.Requests, request =>
            {
                request.ToTable("SpriteRequests");
                request.WithOwner().HasForeignKey(r => r.JobId);
                request.HasKey(r => r.RequestId);
                request.Property(r => r.RequestId).ValueGeneratedNever();
                request.Property(r => r.Kind).HasConversion<string>().HasMaxLength(32);
                request.Property(r => r.Fingerprint).HasMaxLength(64).IsRequired();
                Json(request.Property(r => r.Receipt), "ReceiptJson");
            });
        });
    }

    private static void Json<T>(PropertyBuilder<T> property, string column)
        => property.HasColumnName(column).HasConversion(
            value => SpriteJsonSerializer.Serialize(value),
            json => SpriteJsonSerializer.Deserialize<T>(json));
}
