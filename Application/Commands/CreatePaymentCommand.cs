namespace Application.Commands
{
	public sealed record CreatePaymentCommand(
		int ReservationId,
		decimal Amount,
		string Currency = "EUR");
}
