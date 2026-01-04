namespace Application.StripePayments
{
	public sealed record StripeRefundResult(
		string RefundId,
		string Status,
		decimal Amount,
		string? ErrorCode,
		string? ErrorMessage);
}
