using System.ComponentModel.DataAnnotations;

namespace Application.Commands
{
	public sealed record ConfirmPaymentCommand(
		[Required] string PaymentIntentId,
		[Required] string PaymentMethodId);
}
