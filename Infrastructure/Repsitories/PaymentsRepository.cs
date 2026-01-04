using Application.Services;
using HotelBooking.Payments.Domain.Payments;
using HotelBooking.Payments.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repsitories
{
	public sealed class PaymentsRepository : IPaymentsRepository
	{
		private readonly PaymentsDbContext _context;

		public PaymentsRepository(PaymentsDbContext context)
		{
			_context = context;
		}

		public async Task<Payment?> GetByIdAsync(int id, CancellationToken ct = default)
		{
			return await _context.Payments.FindAsync(new object[] { id }, ct);
		}

		public async Task<List<Payment>> GetByReservationIdAsync(int reservationId, CancellationToken ct = default)
		{
			return await _context.Payments
				.Where(p => p.ReservationId == reservationId)
				.OrderByDescending(p => p.CreatedAt)
				.ToListAsync(ct);
		}

		public async Task AddAsync(Payment payment, CancellationToken ct = default)
		{
			await _context.Payments.AddAsync(payment, ct);
		}

		public async Task SaveChangesAsync(CancellationToken ct = default)
		{
			await _context.SaveChangesAsync(ct);
		}

		public async Task<List<Payment>> GetByPaymentIntentIdAsync(string paymentIntentId, CancellationToken ct = default)
		{
			return await _context.Payments
				.Where(p => p.PaymentIntentId == paymentIntentId)
				.OrderByDescending(p => p.CreatedAt)
				.ToListAsync(ct);
		}
	}
}
