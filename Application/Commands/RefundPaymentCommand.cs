using System.ComponentModel.DataAnnotations;

namespace Application.Commands
{
	public sealed record RefundPaymentCommand(
		[Required] int PaymentId,
		decimal? Amount = null,
		string? Reason = null);
}
