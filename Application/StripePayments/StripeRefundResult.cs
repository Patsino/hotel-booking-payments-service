using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.StripePayments
{
	public sealed record StripeRefundResult(
		string RefundId,
		string Status,
		decimal Amount,
		string? ErrorCode,
		string? ErrorMessage);
}
