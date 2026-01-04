using System.ComponentModel.DataAnnotations;

namespace Application.Commands
{
	public sealed record CreatePaymentIntentCommand(
		[Required] int ReservationId,
		[Range(0.01, double.MaxValue)] decimal Amount,
		[MaxLength(10)] string Currency = "EUR");
}
