using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.Commands
{
	public sealed record CreatePaymentCommand(
		int ReservationId,
		decimal Amount,
		string Currency = "EUR");
}
