using Application.Services;
using HotelBooking.Payments.Domain.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers
{
	[Authorize(Policy = "ServiceToService")]
	[ApiController]
	[Route("internal/payments")]
	public sealed class InternalPaymentsController : ControllerBase
	{
		private readonly IPaymentsRepository _repository;
		private readonly IStripePaymentService _stripeService;
		private readonly IHttpClientFactory _httpClientFactory;
		private readonly ILogger<InternalPaymentsController> _logger;

		public InternalPaymentsController(
			IPaymentsRepository repository,
			IStripePaymentService stripeService,
			IHttpClientFactory httpClientFactory,
			ILogger<InternalPaymentsController> logger)
		{
			_repository = repository;
			_stripeService = stripeService;
			_httpClientFactory = httpClientFactory;
			_logger = logger;
		}

		[HttpPost("reservation/{reservationId}/refund")]
		public async Task<IActionResult> RefundReservation(int reservationId)
		{
			_logger.LogInformation(
				"Internal API: Refund requested for Reservation {ReservationId}",
				reservationId);

			var payments = await _repository.GetByReservationIdAsync(reservationId);
			var payment = payments.FirstOrDefault(p => p.Status == PaymentStatus.Succeeded);

			if (payment == null)
			{
				_logger.LogWarning(
					"Internal API: No successful payment found for Reservation {ReservationId}",
					reservationId);
				return NotFound(new { error = "No successful payment found for this reservation" });
			}

			if (string.IsNullOrEmpty(payment.PaymentIntentId))
			{
				_logger.LogError(
					"Internal API: Payment {PaymentId} has no PaymentIntentId",
					payment.Id);
				return BadRequest(new { error = "Payment has no PaymentIntentId" });
			}

			try
			{
				// Actually call Stripe to create refund
				var refundResult = await _stripeService.CreateRefundAsync(
					payment.PaymentIntentId,
					payment.Amount,
					"requested_by_customer");

				if (refundResult.Status == "succeeded" || refundResult.Status == "pending")
				{
					payment.Refund(payment.Amount);
					await _repository.SaveChangesAsync();

					// Notify Reservations service
					var reservationsClient = _httpClientFactory.CreateClient("ReservationsService");
					await reservationsClient.PostAsync(
						$"/internal/reservations/{reservationId}/mark-canceled-refunded",
						null);

					_logger.LogInformation(
						"Internal API: Refund successful for Payment {PaymentId}, Amount: {Amount}",
						payment.Id, payment.Amount);

					return Ok(new
					{
						success = true,
						refundId = refundResult.RefundId,
						amount = refundResult.Amount
					});
				}
				else
				{
					_logger.LogError(
						"Internal API: Refund failed for Payment {PaymentId}: {Error}",
						payment.Id, refundResult.ErrorMessage);

					return BadRequest(new
					{
						error = $"Refund failed: {refundResult.ErrorMessage}"
					});
				}
			}
			catch (Exception ex)
			{
				_logger.LogError(ex,
					"Internal API: Exception during refund for Payment {PaymentId}",
					payment.Id);

				return StatusCode(500, new
				{
					error = "Refund processing failed",
					details = ex.Message
				});
			}
		}

		[HttpGet("reservation/{reservationId}/summary")]
		public async Task<IActionResult> GetReservationPaymentSummary(int reservationId)
		{
			var payments = await _repository.GetByReservationIdAsync(reservationId);

			if (!payments.Any())
			{
				return NotFound();
			}

			var successfulPayment = payments.FirstOrDefault(p => p.Status == PaymentStatus.Succeeded);
			var totalPaid = payments.Where(p => p.Status == PaymentStatus.Succeeded)
				.Sum(p => p.Amount);
			var totalRefunded = payments.Sum(p => p.AmountRefunded);

			_logger.LogInformation(
				"Internal API: Payment summary for Reservation {ReservationId}",
				reservationId);

			return Ok(new
			{
				reservationId,
				hasSuccessfulPayment = successfulPayment != null,
				totalPaid,
				totalRefunded,
				netAmount = totalPaid - totalRefunded,
				latestStatus = payments.OrderByDescending(p => p.CreatedAt)
					.First().Status.ToString()
			});
		}
	}
}
