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

	public Payment(int reservationId, decimal amount, string currency)
	{
		ReservationId = reservationId;
		Amount = amount;
		Currency = currency;
		CreatedAt = DateTimeOffset.UtcNow;
	}

	private Payment() { }

	public void MarkSucceeded(string providerPaymentId)
	{
		Status = PaymentStatus.Succeeded;
		ProviderPaymentId = providerPaymentId;
		PaidAt = DateTimeOffset.UtcNow;
		ErrorCode = null;
		ErrorMessage = null;
	}

	public void MarkFailed(string? errorCode, string? errorMessage)
	{
		Status = PaymentStatus.Failed;
		ErrorCode = errorCode;
		ErrorMessage = errorMessage;
		IsActive = false;
	}

	public void Refund(decimal amount)
	{
		if (amount > Amount)
		{
			throw new InvalidOperationException("Refund amount cannot exceed payment amount");
		}

		AmountRefunded = amount;
		RefundedAt = DateTimeOffset.UtcNow;
		Status = PaymentStatus.Refunded;
	}

	public void SetPaymentIntent(string paymentIntentId, string clientSecret)
	{
		PaymentIntentId = paymentIntentId;
		Status = PaymentStatus.RequiresPayment;
	}

	public void RequireAction()
	{
		Status = PaymentStatus.RequiresAction;
	}

	public void MarkProcessing()
	{
		Status = PaymentStatus.Processing;
	}

	public void MarkCanceled()
	{
		Status = PaymentStatus.Canceled;
		IsActive = false;
	}

	public void UpdateProviderEventId(string eventId)
	{
		LastProviderEventId = eventId;
	}
}
