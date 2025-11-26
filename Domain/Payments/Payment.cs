namespace HotelBooking.Payments.Domain.Payments;

public sealed class Payment
{
    public int Id { get; private set; }

    public int ReservationId { get; private set; }

    public decimal Amount { get; private set; }

    public string Currency { get; private set; } = null!;

    public PaymentProvider Provider { get; private set; } = PaymentProvider.Stripe;

    public PaymentStatus Status { get; private set; } = PaymentStatus.RequiresPayment;

    public PaymentMethodType? PaymentMethodType { get; private set; }

    public string? PaymentIntentId { get; private set; }

    public string? ProviderPaymentId { get; private set; }

    public decimal AmountRefunded { get; private set; }

    public DateTimeOffset? RefundedAt { get; private set; }

    public DateTimeOffset? PaidAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public bool IsActive { get; private set; } = true;

    public string? LastProviderEventId { get; private set; }

    public string? ErrorCode { get; private set; }

    public string? ErrorMessage { get; private set; }

    public Payment(
        int reservationId,
        decimal amount,
        string currency)
    {
        ReservationId = reservationId;
        Amount = amount;
        Currency = currency;
    }

    private Payment()
    {
    }
}
