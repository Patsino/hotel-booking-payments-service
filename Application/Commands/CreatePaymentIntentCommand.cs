using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.Commands
{
	public sealed record CreatePaymentIntentCommand(
		[Required] int ReservationId,
		[Range(0.01, double.MaxValue)] decimal Amount,
		[MaxLength(10)] string Currency = "EUR");
}
