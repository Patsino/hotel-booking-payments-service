using Application.Commands;
using Application.Dtos;
using Application.Handlers;
using Application.Services;
using HotelBooking.Payments.Domain.Payments;
using Infrastructure.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers
{
	[ApiController]
	[Route("api/payments")]
	public sealed class PaymentsController : ControllerBase
	{
		private readonly IPaymentsRepository _repository;
		private readonly CreatePaymentIntentHandler _createIntentHandler;
		private readonly ConfirmPaymentHandler _confirmHandler;
		private readonly RefundPaymentHandler _refundHandler;
		private readonly IHttpClientFactory _httpClientFactory;
		private readonly ICurrentUserService _currentUser;
		private readonly ILogger<PaymentsController> _logger;

		public PaymentsController(
			IPaymentsRepository repository,
			CreatePaymentIntentHandler createIntentHandler,
			ConfirmPaymentHandler confirmHandler,
			RefundPaymentHandler refundHandler,
			IHttpClientFactory httpClientFactory,
			ICurrentUserService currentUser,
			ILogger<PaymentsController> logger)
		{
			_repository = repository;
			_createIntentHandler = createIntentHandler;
			_confirmHandler = confirmHandler;
			_refundHandler = refundHandler;
			_httpClientFactory = httpClientFactory;
			_currentUser = currentUser;
			_logger = logger;
		}

		[Authorize(Policy = "RegisteredUser")]
		[HttpPost("create-intent")]
		public async Task<IActionResult> CreatePaymentIntent([FromBody] CreatePaymentIntentCommand command)
		{
			try
			{
				// Verify user owns the reservation
				var reservationsClient = _httpClientFactory.CreateClient("ReservationsService");
				var reservationResponse = await reservationsClient.GetAsync(
					$"/internal/reservations/{command.ReservationId}");

				if (!reservationResponse.IsSuccessStatusCode)
					return BadRequest(new { error = "Reservation not found" });

				var reservationData = await reservationResponse.Content.ReadFromJsonAsync<ReservationDto>();

				if (!_currentUser.IsAdmin && reservationData?.UserId != _currentUser.UserId)
				{
					return Forbid();
				}

				var result = await _createIntentHandler.HandleAsync(command);
				_logger.LogInformation("Payment intent created: {PaymentId} by User {UserId}",
					result.PaymentId, _currentUser.UserId);
				return Ok(result);
			}
			catch (InvalidOperationException ex)
			{
				return BadRequest(new { error = ex.Message });
			}
		}

		[Authorize(Policy = "RegisteredUser")]
		[HttpPost("confirm")]
		public async Task<IActionResult> ConfirmPayment([FromBody] ConfirmPaymentCommand command)
		{
			try
			{
				// Verify user owns the payment
				var payments = await _repository.GetByPaymentIntentIdAsync(command.PaymentIntentId);
				var payment = payments.FirstOrDefault();

				if (payment == null)
					return BadRequest(new { error = "Payment not found" });

				// Check reservation ownership
				var reservationsClient = _httpClientFactory.CreateClient("ReservationsService");
				var reservationResponse = await reservationsClient.GetAsync(
					$"/internal/reservations/{payment.ReservationId}");

				if (!reservationResponse.IsSuccessStatusCode)
					return BadRequest(new { error = "Reservation not found" });

				var reservationData = await reservationResponse.Content.ReadFromJsonAsync<ReservationDto>();

				if (!_currentUser.IsAdmin && reservationData?.UserId != _currentUser.UserId)
				{
					return Forbid();
				}

				await _confirmHandler.HandleAsync(command);
				return Ok(new { message = "Payment confirmed successfully" });
			}
			catch (InvalidOperationException ex)
			{
				return BadRequest(new { error = ex.Message });
			}
		}

		[Authorize(Policy = "AdminOnly")]
		[HttpPost("{id}/refund")]
		public async Task<IActionResult> RefundPayment(int id, [FromBody] RefundPaymentCommand command)
		{
			try
			{
				await _refundHandler.HandleAsync(command with { PaymentId = id });
				_logger.LogInformation("Refund processed for payment: {PaymentId} by Admin", id);
				return Ok(new { message = "Refund processed successfully" });
			}
			catch (InvalidOperationException ex)
			{
				return BadRequest(new { error = ex.Message });
			}
		}

		[Authorize(Policy = "RegisteredUser")]
		[HttpGet("{id}")]
		public async Task<IActionResult> GetById(int id)
		{
			var payment = await _repository.GetByIdAsync(id);
			if (payment == null)
				return NotFound();

			// Check reservation ownership
			var reservationsClient = _httpClientFactory.CreateClient("ReservationsService");
			var reservationResponse = await reservationsClient.GetAsync(
				$"/internal/reservations/{payment.ReservationId}");

			if (reservationResponse.IsSuccessStatusCode)
			{
				var reservationData = await reservationResponse.Content.ReadFromJsonAsync<ReservationDto>();

				if (!_currentUser.IsAdmin && reservationData?.UserId != _currentUser.UserId)
				{
					return Forbid();
				}
			}

			return Ok(new
			{
				payment.Id,
				payment.ReservationId,
				payment.Amount,
				payment.Currency,
				payment.PaymentIntentId,
				Status = payment.Status.ToString(),
				payment.AmountRefunded,
				payment.PaidAt,
				payment.RefundedAt,
				payment.CreatedAt,
				payment.ErrorCode,
				payment.ErrorMessage
			});
		}

		[Authorize(Policy = "RegisteredUser")]
		[HttpGet("reservation/{reservationId}")]
		public async Task<IActionResult> GetByReservation(int reservationId)
		{
			// Check reservation ownership
			var reservationsClient = _httpClientFactory.CreateClient("ReservationsService");
			var reservationResponse = await reservationsClient.GetAsync(
				$"/internal/reservations/{reservationId}");

			if (!reservationResponse.IsSuccessStatusCode)
				return NotFound(new { error = "Reservation not found" });

			var reservationData = await reservationResponse.Content.ReadFromJsonAsync<ReservationDto>();

			if (!_currentUser.IsAdmin && reservationData?.UserId != _currentUser.UserId)
			{
				return Forbid();
			}

			var payments = await _repository.GetByReservationIdAsync(reservationId);
			return Ok(payments.Select(p => new
			{
				p.Id,
				p.Amount,
				p.Currency,
				Status = p.Status.ToString(),
				p.PaidAt,
				p.CreatedAt
			}));
		}
	}
}
