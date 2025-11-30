using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.StripePayments
{
	public sealed record StripePaymentResult(
		string PaymentIntentId,
		string Status,
		string? PaymentMethodId,
		string? ErrorCode,
		string? ErrorMessage);
}
