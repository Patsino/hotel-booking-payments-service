using HotelBooking.Payments.Domain.Payments;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.Services
{
	public interface IPaymentsRepository
	{
		Task<Payment?> GetByIdAsync(int id, CancellationToken ct = default);
		Task<List<Payment>> GetByReservationIdAsync(int reservationId, CancellationToken ct = default);
		Task AddAsync(Payment payment, CancellationToken ct = default);
		Task SaveChangesAsync(CancellationToken ct = default);
		Task<List<Payment>> GetByPaymentIntentIdAsync(string paymentIntentId, CancellationToken ct = default);
	}
}
