namespace Application.Dtos
{
	public sealed record PaymentIntentResponse(
		int PaymentId,
		string PaymentIntentId,
		string ClientSecret,
		decimal Amount,
		string Currency);
}
