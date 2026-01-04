using Application.Commands;
using Application.Services;
using HotelBooking.Payments.Domain.Payments;

namespace Application.Handlers
{
	public sealed class CreatePaymentHandler
	{
		private readonly IPaymentsRepository _repository;

		public CreatePaymentHandler(IPaymentsRepository repository)
		{
			_repository = repository;
		}

		public async Task<int> HandleAsync(CreatePaymentCommand command, CancellationToken ct = default)
		{
			var payment = new Payment(command.ReservationId, command.Amount, command.Currency);

			await _repository.AddAsync(payment, ct);
			await _repository.SaveChangesAsync(ct);

			return payment.Id;
		}
	}
}
