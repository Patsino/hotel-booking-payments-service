using Application.Services;
using Azure.Core;
using Microsoft.AspNetCore.Mvc;
using Stripe;
using Swashbuckle.AspNetCore.Annotations;
using System.Diagnostics.CodeAnalysis;

namespace Api.Controllers
{
	[ExcludeFromCodeCoverage]
	[ApiController]
	[Route("api/webhooks")]
	public sealed class StripeWebhookController : ControllerBase
	{
		private readonly IPaymentsRepository _repository;
		private readonly IHttpClientFactory _httpClientFactory;
		private readonly IConfiguration _configuration;
		private readonly ILogger<StripeWebhookController> _logger;

		public StripeWebhookController(
			IPaymentsRepository repository,
			IHttpClientFactory httpClientFactory,
			IConfiguration configuration,
			ILogger<StripeWebhookController> logger)
		{
			_repository = repository;
			_httpClientFactory = httpClientFactory;
			_configuration = configuration;
			_logger = logger;
		}

		/// <summary>
		/// Stripe webhook endpoint for payment events
		/// </summary>
		/// <returns>OK if webhook processed successfully</returns>
		/// <remarks>
		/// Receives webhook events from Stripe for payment status updates.
		/// Webhook signature is validated using Stripe webhook secret from configuration.
		/// 
		/// **No request body required** - Stripe sends the event data automatically.
		/// 
		/// **Handled events:**
		/// - **payment_intent.succeeded**: Payment completed successfully
		/// - **payment_intent.payment_failed**: Payment failed
		/// - **payment_intent.canceled**: Payment intent canceled
		/// - **payment_intent.processing**: Payment is being processed
		/// - **payment_intent.requires_action**: Additional action required (e.g., 3D Secure)
		/// - **charge.refunded**: Refund completed
		/// 
		/// **Actions performed:**
		/// - Updates payment status in database
		/// - Notifies Reservations Service of payment status changes
		/// - Logs all events for audit trail
		/// </remarks>
		/// <response code="200">Webhook processed successfully</response>
		/// <response code="400">Invalid signature or webhook secret not configured</response>
		/// <response code="500">Webhook processing failed</response>
		[HttpPost("stripe")]
		[SwaggerOperation(Summary = "Stripe webhook", Description = "Receive Stripe payment events", OperationId = "StripeWebhook", Tags = new[] { "Webhooks" })]
		[SwaggerResponse(200, "Webhook processed")]
		[SwaggerResponse(400, "Invalid signature")]
		[SwaggerResponse(500, "Processing failed")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status500InternalServerError)]
		public async Task<IActionResult> HandleStripeWebhook()
		{
			var json = await new StreamReader(HttpContext.Request.Body).ReadToEndAsync();
			var webhookSecret = _configuration["Stripe:WebhookSecret"];

			if (string.IsNullOrEmpty(webhookSecret))
			{
				_logger.LogWarning("Stripe webhook secret not configured");
				return BadRequest("Webhook secret not configured");
			}

			try
			{
				// Verify webhook signature
				var stripeSignature = Request.Headers["Stripe-Signature"].ToString();
				var stripeEvent = EventUtility.ConstructEvent(
					json,
					stripeSignature,
					webhookSecret,
					throwOnApiVersionMismatch: false);

				_logger.LogInformation(
					"Received Stripe webhook: {EventType}, EventId: {EventId}",
				stripeEvent.Type, stripeEvent.Id);

				// Handle different event types
				switch (stripeEvent.Type)
				{
					case Events.PaymentIntentSucceeded:
						await HandlePaymentIntentSucceeded(stripeEvent);
						break;

					case Events.PaymentIntentPaymentFailed:
						await HandlePaymentIntentFailed(stripeEvent);
						break;

					case Events.PaymentIntentCanceled:
						await HandlePaymentIntentCanceled(stripeEvent);
						break;

					case Events.PaymentIntentProcessing:
						await HandlePaymentIntentProcessing(stripeEvent);
						break;

					case Events.PaymentIntentRequiresAction:
						await HandlePaymentIntentRequiresAction(stripeEvent);
						break;

					case Events.ChargeRefunded:
						await HandleChargeRefunded(stripeEvent);
						break;

					default:
						_logger.LogInformation("Unhandled event type: {EventType}", stripeEvent.Type);
						break;
				}

				return Ok(new { received = true });
			}
			catch (StripeException ex)
			{
				_logger.LogError(ex, "Stripe webhook signature verification failed");
				return BadRequest(new { error = "Invalid signature" });
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error processing Stripe webhook");
				return StatusCode(500, new { error = "Webhook processing failed" });
			}
		}

		private async Task HandlePaymentIntentSucceeded(Event stripeEvent)
		{
			var paymentIntent = stripeEvent.Data.Object as PaymentIntent;
			if (paymentIntent == null) return;

			var payments = await _repository.GetByPaymentIntentIdAsync(paymentIntent.Id);
			var payment = payments.FirstOrDefault();

			if (payment == null)
			{
				_logger.LogWarning("Payment not found for PaymentIntent: {PaymentIntentId}", paymentIntent.Id);
				return;
			}

			payment.MarkSucceeded(paymentIntent.LatestCharge?.Id ?? paymentIntent.Id);
			payment.UpdateProviderEventId(stripeEvent.Id);

			await _repository.SaveChangesAsync();

			// Notify Reservations service
			try
			{
				var reservationsClient = _httpClientFactory.CreateClient("ReservationsService");
				var response = await reservationsClient.PostAsync(
					$"/internal/reservations/{payment.ReservationId}/mark-confirmed",
					null);

				if (!response.IsSuccessStatusCode)
				{
					_logger.LogError(
						"Failed to confirm reservation {ReservationId} after payment success",
						payment.ReservationId);
				}
			}
			catch (Exception ex)
			{
				_logger.LogError(ex,
					"Error notifying reservations service for payment {PaymentId}",
					payment.Id);
			}

			_logger.LogInformation(
				"Payment succeeded: PaymentId={PaymentId}, PaymentIntentId={PaymentIntentId}, Amount={Amount}",
				payment.Id, paymentIntent.Id, payment.Amount);
		}

		private async Task HandlePaymentIntentFailed(Event stripeEvent)
		{
			var paymentIntent = stripeEvent.Data.Object as PaymentIntent;
			if (paymentIntent == null) return;

			var payments = await _repository.GetByPaymentIntentIdAsync(paymentIntent.Id);
			var payment = payments.FirstOrDefault();

			if (payment == null)
			{
				_logger.LogWarning("Payment not found for PaymentIntent: {PaymentIntentId}", paymentIntent.Id);
				return;
			}

			var error = paymentIntent.LastPaymentError;
			payment.MarkFailed(error?.Code, error?.Message);
			payment.UpdateProviderEventId(stripeEvent.Id);

			await _repository.SaveChangesAsync();

			_logger.LogWarning(
				"Payment failed: PaymentId={PaymentId}, PaymentIntentId={PaymentIntentId}, Error={ErrorCode}: {ErrorMessage}",
				payment.Id, paymentIntent.Id, error?.Code, error?.Message);
		}

		private async Task HandlePaymentIntentCanceled(Event stripeEvent)
		{
			var paymentIntent = stripeEvent.Data.Object as PaymentIntent;
			if (paymentIntent == null) return;

			var payments = await _repository.GetByPaymentIntentIdAsync(paymentIntent.Id);
			var payment = payments.FirstOrDefault();

			if (payment == null)
			{
				_logger.LogWarning("Payment not found for PaymentIntent: {PaymentIntentId}", paymentIntent.Id);
				return;
			}

			payment.MarkCanceled();
			payment.UpdateProviderEventId(stripeEvent.Id);

			await _repository.SaveChangesAsync();

			_logger.LogInformation(
				"Payment canceled: PaymentId={PaymentId}, PaymentIntentId={PaymentIntentId}",
				payment.Id, paymentIntent.Id);
		}

		private async Task HandlePaymentIntentProcessing(Event stripeEvent)
		{
			var paymentIntent = stripeEvent.Data.Object as PaymentIntent;
			if (paymentIntent == null) return;

			var payments = await _repository.GetByPaymentIntentIdAsync(paymentIntent.Id);
			var payment = payments.FirstOrDefault();

			if (payment == null) return;

			payment.MarkProcessing();
			payment.UpdateProviderEventId(stripeEvent.Id);

			await _repository.SaveChangesAsync();

			_logger.LogInformation(
				"Payment processing: PaymentId={PaymentId}, PaymentIntentId={PaymentIntentId}",
				payment.Id, paymentIntent.Id);
		}

		private async Task HandlePaymentIntentRequiresAction(Event stripeEvent)
		{
			var paymentIntent = stripeEvent.Data.Object as PaymentIntent;
			if (paymentIntent == null) return;

			var payments = await _repository.GetByPaymentIntentIdAsync(paymentIntent.Id);
			var payment = payments.FirstOrDefault();

			if (payment == null) return;

			payment.RequireAction();
			payment.UpdateProviderEventId(stripeEvent.Id);

			await _repository.SaveChangesAsync();

			_logger.LogInformation(
				"Payment requires action: PaymentId={PaymentId}, PaymentIntentId={PaymentIntentId}",
				payment.Id, paymentIntent.Id);
		}

		private async Task HandleChargeRefunded(Event stripeEvent)
		{
			var charge = stripeEvent.Data.Object as Charge;
			if (charge == null) return;

			var paymentIntentId = charge.PaymentIntentId;
			if (string.IsNullOrEmpty(paymentIntentId)) return;

			var payments = await _repository.GetByPaymentIntentIdAsync(paymentIntentId);
			var payment = payments.FirstOrDefault();

			if (payment == null)
			{
				_logger.LogWarning("Payment not found for refunded charge: {ChargeId}", charge.Id);
				return;
			}

			var refundedAmount = charge.AmountRefunded / 100m;
			payment.Refund(refundedAmount);
			payment.UpdateProviderEventId(stripeEvent.Id);

			await _repository.SaveChangesAsync();

			_logger.LogInformation(
				"Charge refunded: PaymentId={PaymentId}, ChargeId={ChargeId}, Amount={Amount}",
				payment.Id, charge.Id, refundedAmount);
		}
	}
}
