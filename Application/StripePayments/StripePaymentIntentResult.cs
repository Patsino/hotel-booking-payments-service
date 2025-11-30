using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.StripePayments
{
	public sealed record StripePaymentIntentResult(
		string PaymentIntentId,
		string ClientSecret,
		string Status,
		decimal Amount,
		string Currency);
}
