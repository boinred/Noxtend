using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Noxtend.Tuning.Domain.Call;

namespace Noxtend.Infrastructure.Persistence.Configurations;

public sealed class PriceUpdateSnapshotConfiguration : IEntityTypeConfiguration<PriceUpdateSnapshot>
{
    public void Configure(EntityTypeBuilder<PriceUpdateSnapshot> builder)
    {
        builder.ToTable("PriceUpdateSnapshots");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Json).IsRequired();
    }
}

public sealed class PriceUpdateRequestReceiptConfiguration : IEntityTypeConfiguration<PriceUpdateRequestReceipt>
{
    public void Configure(EntityTypeBuilder<PriceUpdateRequestReceipt> builder)
    {
        builder.ToTable("PriceUpdateRequestReceipts");
        builder.HasKey(x => x.RequestId);
        builder.Property(x => x.RequestJson).IsRequired();
        builder.Property(x => x.ReceiptJson).IsRequired();
    }
}
