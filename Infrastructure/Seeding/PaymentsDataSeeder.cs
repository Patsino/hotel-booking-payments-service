using HotelBooking.Payments.Domain.Payments;
using HotelBooking.Payments.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Seeding
{
	public sealed class PaymentsDataSeeder
	{
		private readonly PaymentsDbContext _context;
		private readonly ILogger<PaymentsDataSeeder> _logger;

		public PaymentsDataSeeder(PaymentsDbContext context, ILogger<PaymentsDataSeeder> logger)
		{
			_context = context;
			_logger = logger;
		}

		public async Task SeedAsync()
		{
			if (await _context.Payments.AnyAsync())
			{
				_logger.LogInformation("Payments already seeded, skipping");
				return;
			}

			_logger.LogInformation("Seeding payments...");

			var payments = new List<Payment>
		{
            // Payment for reservation 1 (completed)
            new Payment(1, 449.95m, "EUR"),

            // Payment for reservation 2 (completed)
            new Payment(2, 999.95m, "EUR"),

            // Payment for reservation 3 (completed)
            new Payment(3, 479.97m, "EUR"),

            // Payment for reservation 5 (completed)
            new Payment(5, 499.95m, "EUR"),

            // Payment for reservation 6 (refunded)
            new Payment(6, 159.98m, "EUR")
		};

			// Set payment statuses
			payments[0].MarkSucceeded("ch_test_past_payment");
			payments[1].MarkSucceeded("ch_test_current_payment");
			payments[2].MarkSucceeded("ch_test_upcoming_payment");
			payments[3].MarkSucceeded("ch_test_future_payment");

			payments[4].MarkSucceeded("ch_test_canceled_payment");
			payments[4].Refund(159.98m); // Refunded

			await _context.Payments.AddRangeAsync(payments);
			await _context.SaveChangesAsync();

			_logger.LogInformation("Seeded {Count} payments", payments.Count);
		}
	}
}
