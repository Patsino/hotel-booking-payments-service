using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.Commands
{
	public sealed record RefundPaymentCommand(
		[Required] int PaymentId,
		decimal? Amount = null,
		string? Reason = null);
}
