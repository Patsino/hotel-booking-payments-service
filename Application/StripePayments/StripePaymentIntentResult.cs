namespace Application.StripePayments
{
	public sealed record StripePaymentIntentResult(
		string PaymentIntentId,
		string ClientSecret,
		string Status,
		decimal Amount,
		string Currency);
}
