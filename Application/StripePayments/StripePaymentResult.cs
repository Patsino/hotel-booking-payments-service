namespace Application.StripePayments
{
	public sealed record StripePaymentResult(
		string PaymentIntentId,
		string Status,
		string? PaymentMethodId,
		string? ErrorCode,
		string? ErrorMessage);
}
