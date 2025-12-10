using Application.Commands;
using Application.Dtos;
using Application.Handlers;
using Application.Services;
using HotelBooking.Payments.Domain.Payments;
using Infrastructure.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Api.Controllers
{
	[ApiController]
	[Route("api/payments")]
	public sealed class PaymentsController : ControllerBase
	{
		private readonly IPaymentsRepository _repository;
		private readonly ICreatePaymentIntentHandler _createIntentHandler;
		private readonly IConfirmPaymentHandler _confirmHandler;
		private readonly IRefundPaymentHandler _refundHandler;
		private readonly IHttpClientFactory _httpClientFactory;
		private readonly ICurrentUserService _currentUser;
		private readonly ILogger<PaymentsController> _logger;

		public PaymentsController(
			IPaymentsRepository repository,
			ICreatePaymentIntentHandler createIntentHandler,
			IConfirmPaymentHandler confirmHandler,
			IRefundPaymentHandler refundHandler,
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

		/// <summary>
		/// Create Stripe payment intent for a reservation
		/// </summary>
		/// <param name="command">Payment details including reservation ID, amount, and currency</param>
		/// <returns>Payment intent with client secret for frontend confirmation</returns>
		/// <remarks>
		/// Creates a Stripe payment intent. Returns client secret for confirming payment on frontend with Stripe.js.
		/// 
		/// Sample request:
		/// 
		///     POST /api/payments/create-intent
		///     {
		///        "reservationId": 150,
		///        "amount": 178.00,
		///        "currency": "EUR"
		///     }
		/// 
		/// **Response includes:**
		/// - **paymentIntentId**: Stripe payment intent ID
		/// - **clientSecret**: Use with Stripe.js to confirm payment
		/// - **paymentId**: Internal payment record ID
		/// 
		/// **Validation:**
		/// - amount: minimum €0.01
		/// - currency: max 10 characters (default: EUR)
		/// - User must own the reservation
		/// </remarks>
		/// <response code="200">Payment intent created successfully</response>
		/// <response code="400">Invalid reservation, payment already exists, or invalid amount</response>
		/// <response code="401">User not authenticated</response>
		/// <response code="403">User trying to pay for another user's reservation</response>
		[Authorize(Policy = "RegisteredUser")]
		[HttpPost("create-intent")]
		[SwaggerOperation(Summary = "Create payment intent", Description = "Create Stripe payment intent for reservation", OperationId = "CreatePaymentIntent", Tags = new[] { "Payments" })]
		[SwaggerResponse(200, "Payment intent created")]
		[SwaggerResponse(400, "Invalid request or reservation")]
		[SwaggerResponse(401, "Unauthorized")]
		[SwaggerResponse(403, "Forbidden")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		[ProducesResponseType(StatusCodes.Status403Forbidden)]
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

		/// <summary>
		/// Confirm payment after frontend authorization
		/// </summary>
		/// <param name="command">Payment intent ID and payment method ID from Stripe</param>
		/// <returns>Confirmation message</returns>
		/// <remarks>
		/// Confirms a payment after user has authorized it on the frontend using Stripe.js.
		/// 
		/// Sample request:
		/// 
		///     POST /api/payments/confirm
		///     {
		///        "paymentIntentId": "pi_1234567890abcdef",
		///        "paymentMethodId": "pm_1234567890abcdef"
		///     }
		/// 
		/// **Validation:**
		/// - User must own the reservation associated with the payment
		/// </remarks>
		/// <response code="200">Payment confirmed successfully</response>
		/// <response code="400">Payment not found or invalid payment intent</response>
		/// <response code="401">User not authenticated</response>
		/// <response code="403">User trying to confirm another user's payment</response>
		[Authorize(Policy = "RegisteredUser")]
		[HttpPost("confirm")]
		[SwaggerOperation(Summary = "Confirm payment", Description = "Confirm payment after frontend authorization", OperationId = "ConfirmPayment", Tags = new[] { "Payments" })]
		[SwaggerResponse(200, "Payment confirmed")]
		[SwaggerResponse(400, "Invalid payment intent")]
		[SwaggerResponse(401, "Unauthorized")]
		[SwaggerResponse(403, "Forbidden")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		[ProducesResponseType(StatusCodes.Status403Forbidden)]
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

		/// <summary>
		/// Initiate refund for a payment (Admin only)
		/// </summary>
		/// <param name="command">Refund details including payment ID, amount, and reason</param>
		/// <returns>Refund confirmation message</returns>
		/// <remarks>
		/// Initiates a refund through Stripe. Can be full or partial refund.
		/// 
		/// Sample request (full refund):
		/// 
		///     POST /api/payments/refund
		///     {
		///        "paymentId": 88,
		///        "amount": null,
		///        "reason": "Cancellation within free period"
		///     }
		/// 
		/// Sample request (partial refund):
		/// 
		///     POST /api/payments/refund
		///     {
		///        "paymentId": 88,
		///        "amount": 89.00,
		///        "reason": "Service issue compensation"
		///     }
		/// 
		/// **Validation:**
		/// - Payment must be in Succeeded status
		/// - If amount is null, full refund is performed
		/// - Partial refund amount cannot exceed remaining balance
		/// </remarks>
		/// <response code="200">Refund initiated successfully</response>
		/// <response code="400">Cannot refund payment (wrong status or insufficient balance)</response>
		/// <response code="401">User not authenticated</response>
		/// <response code="403">User is not Admin</response>
		/// <response code="404">Payment not found</response>
		[Authorize(Policy = "AdminOnly")]
		[HttpPost("refund")]
		[SwaggerOperation(Summary = "Refund payment", Description = "Initiate full or partial refund", OperationId = "RefundPayment", Tags = new[] { "Payments - Admin" })]
		[SwaggerResponse(200, "Refund processed")]
		[SwaggerResponse(400, "Cannot refund")]
		[SwaggerResponse(401, "Unauthorized")]
		[SwaggerResponse(403, "Forbidden - Admin role required")]
		[SwaggerResponse(404, "Payment not found")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		[ProducesResponseType(StatusCodes.Status403Forbidden)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<IActionResult> RefundPayment([FromBody] RefundPaymentCommand command)
		{
			try
			{
				await _refundHandler.HandleAsync(command);
				_logger.LogInformation("Refund processed for payment: {PaymentId} by Admin", command.PaymentId);
				return Ok(new { message = "Refund processed successfully" });
			}
			catch (InvalidOperationException ex)
			{
				return BadRequest(new { error = ex.Message });
			}
		}

		/// <summary>
		/// Get payment by ID
		/// </summary>
		/// <param name="id">Payment ID</param>
		/// <returns>Payment details</returns>
		/// <remarks>
		/// Returns payment details. Users can only view payments for their own reservations.
		/// 
		/// **Response includes:**
		/// - id, reservationId, amount, currency
		/// - paymentIntentId (Stripe ID)
		/// - status (Pending, Processing, Succeeded, Failed, Cancelled, Refunded)
		/// - amountRefunded
		/// - paidAt, refundedAt, createdAt timestamps
		/// - errorCode, errorMessage (if failed)
		/// </remarks>
		/// <response code="200">Payment details retrieved</response>
		/// <response code="401">User not authenticated</response>
		/// <response code="403">User trying to access another user's payment</response>
		/// <response code="404">Payment not found</response>
		[Authorize(Policy = "RegisteredUser")]
		[HttpGet("{id}")]
		[SwaggerOperation(Summary = "Get payment by ID", Description = "Retrieve specific payment details", OperationId = "GetPaymentById", Tags = new[] { "Payments" })]
		[SwaggerResponse(200, "Payment details")]
		[SwaggerResponse(401, "Unauthorized")]
		[SwaggerResponse(403, "Forbidden")]
		[SwaggerResponse(404, "Payment not found")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		[ProducesResponseType(StatusCodes.Status403Forbidden)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
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

		/// <summary>
		/// Get all payments for a specific reservation
		/// </summary>
		/// <param name="reservationId">Reservation ID</param>
		/// <returns>List of payments for the reservation</returns>
		/// <remarks>
		/// Returns all payment attempts for a reservation. Users can only view payments for their own reservations.
		/// 
		/// **Response includes:** id, amount, currency, status, paidAt, createdAt
		/// </remarks>
		/// <response code="200">List of payments</response>
		/// <response code="401">User not authenticated</response>
		/// <response code="403">User trying to access another user's payments</response>
		/// <response code="404">Reservation not found</response>
		[Authorize(Policy = "RegisteredUser")]
		[HttpGet("reservation/{reservationId}")]
		[SwaggerOperation(Summary = "Get payments for reservation", Description = "Retrieve all payments for a specific reservation", OperationId = "GetPaymentsByReservation", Tags = new[] { "Payments" })]
		[SwaggerResponse(200, "List of payments")]
		[SwaggerResponse(401, "Unauthorized")]
		[SwaggerResponse(403, "Forbidden")]
		[SwaggerResponse(404, "Reservation not found")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		[ProducesResponseType(StatusCodes.Status403Forbidden)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
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
