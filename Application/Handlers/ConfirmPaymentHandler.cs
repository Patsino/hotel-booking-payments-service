using Application.Commands;
using Application.Services;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.Handlers
{
	public sealed class ConfirmPaymentHandler
	{
		private readonly IPaymentsRepository _repository;
		private readonly IStripePaymentService _stripeService;
		private readonly IHttpClientFactory _httpClientFactory;
		private readonly ILogger<ConfirmPaymentHandler> _logger;

		public ConfirmPaymentHandler(
			IPaymentsRepository repository,
			IStripePaymentService stripeService,
			IHttpClientFactory httpClientFactory,
			ILogger<ConfirmPaymentHandler> logger)
		{
			_repository = repository;
			_stripeService = stripeService;
			_httpClientFactory = httpClientFactory;
			_logger = logger;
		}

		public async Task HandleAsync(ConfirmPaymentCommand command, CancellationToken ct = default)
		{
			// Find payment by PaymentIntentId
			var payments = await _repository.GetByPaymentIntentIdAsync(command.PaymentIntentId, ct);
			var payment = payments.FirstOrDefault();

			if (payment == null)
			{
				throw new InvalidOperationException("Payment not found");
			}

			// Confirm with Stripe
			var result = await _stripeService.ConfirmPaymentAsync(
				command.PaymentIntentId,
				command.PaymentMethodId,
				ct);

			// Update payment based on result
			if (result.Status == "succeeded")
			{
				payment.MarkSucceeded(result.PaymentMethodId ?? command.PaymentMethodId);

				// Notify Reservations service
				var reservationsClient = _httpClientFactory.CreateClient("ReservationsService");
				await reservationsClient.PostAsync(
					$"/internal/reservations/{payment.ReservationId}/mark-confirmed",
					null,
					ct);

				_logger.LogInformation(
					"Payment confirmed successfully: PaymentId={PaymentId}, PaymentIntentId={PaymentIntentId}",
					payment.Id, command.PaymentIntentId);
			}
			else if (result.Status == "requires_action")
			{
				payment.RequireAction();
				_logger.LogInformation(
					"Payment requires additional action: PaymentId={PaymentId}",
					payment.Id);
			}
			else
			{
				payment.MarkFailed(result.ErrorCode, result.ErrorMessage);
				_logger.LogWarning(
					"Payment failed: PaymentId={PaymentId}, Error={Error}",
					payment.Id, result.ErrorMessage);
			}

			await _repository.SaveChangesAsync(ct);
		}
	}
}
