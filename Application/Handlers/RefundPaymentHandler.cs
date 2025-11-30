using Application.Commands;
using Application.Services;
using HotelBooking.Payments.Domain.Payments;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.Handlers
{
	public sealed class RefundPaymentHandler
	{
		private readonly IPaymentsRepository _repository;
		private readonly IStripePaymentService _stripeService;
		private readonly IHttpClientFactory _httpClientFactory;
		private readonly ILogger<RefundPaymentHandler> _logger;

		public RefundPaymentHandler(
			IPaymentsRepository repository,
			IStripePaymentService stripeService,
			IHttpClientFactory httpClientFactory,
			ILogger<RefundPaymentHandler> logger)
		{
			_repository = repository;
			_stripeService = stripeService;
			_httpClientFactory = httpClientFactory;
			_logger = logger;
		}

		public async Task HandleAsync(RefundPaymentCommand command, CancellationToken ct = default)
		{
			var payment = await _repository.GetByIdAsync(command.PaymentId, ct);

			if (payment == null)
			{
				throw new InvalidOperationException("Payment not found");
			}

			if (payment.Status != PaymentStatus.Succeeded)
			{
				throw new InvalidOperationException("Can only refund succeeded payments");
			}

			if (string.IsNullOrEmpty(payment.PaymentIntentId))
			{
				throw new InvalidOperationException("Payment has no PaymentIntentId");
			}

			// Create refund in Stripe
			var refundResult = await _stripeService.CreateRefundAsync(
				payment.PaymentIntentId,
				command.Amount,
				command.Reason,
				ct);

			if (refundResult.Status == "succeeded" || refundResult.Status == "pending")
			{
				payment.Refund(command.Amount ?? payment.Amount);

				// Notify Reservations service
				var reservationsClient = _httpClientFactory.CreateClient("ReservationsService");
				await reservationsClient.PostAsync(
					$"/internal/reservations/{payment.ReservationId}/mark-canceled-refunded",
					null,
					ct);

				_logger.LogInformation(
					"Refund processed: PaymentId={PaymentId}, RefundId={RefundId}, Amount={Amount}",
					payment.Id, refundResult.RefundId, refundResult.Amount);
			}
			else
			{
				throw new InvalidOperationException($"Refund failed: {refundResult.ErrorMessage}");
			}

			await _repository.SaveChangesAsync(ct);
		}
	}
}
