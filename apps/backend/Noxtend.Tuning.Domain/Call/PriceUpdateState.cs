namespace Noxtend.Tuning.Domain.Call;

public sealed class PriceUpdateSnapshot
{
    public Guid Id { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public string Json { get; set; } = string.Empty;
}

public sealed class PriceUpdateRequestReceipt
{
    public Guid RequestId { get; set; }
    public string RequestJson { get; set; } = string.Empty;
    public string ReceiptJson { get; set; } = string.Empty;
}
