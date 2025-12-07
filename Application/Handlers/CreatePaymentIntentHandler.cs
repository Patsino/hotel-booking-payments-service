using Application.Commands;
using Application.Dtos;
using Application.Services;
using HotelBooking.Payments.Domain.Payments;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Threading.Tasks;

namespace Application.Handlers
{
	public sealed class CreatePaymentIntentHandler : ICreatePaymentIntentHandler
	{
		private readonly IPaymentsRepository _repository;
		private readonly IStripePaymentService _stripeService;
		private readonly IHttpClientFactory _httpClientFactory;
		private readonly ILogger<CreatePaymentIntentHandler> _logger;
		private readonly IHttpContextAccessor _httpContextAccessor;

		public CreatePaymentIntentHandler(
			IPaymentsRepository repository,
			IStripePaymentService stripeService,
			IHttpClientFactory httpClientFactory,
			ILogger<CreatePaymentIntentHandler> logger,
			IHttpContextAccessor httpContextAccessor)
		{
			_repository = repository;
			_stripeService = stripeService;
			_httpClientFactory = httpClientFactory;
			_logger = logger;
			_httpContextAccessor = httpContextAccessor;
		}

		public async Task<PaymentIntentResponse> HandleAsync(
			CreatePaymentIntentCommand command,
			CancellationToken ct = default)
		{
			// Verify reservation exists and is valid
			var reservationsClient = _httpClientFactory.CreateClient("ReservationsService");

			var token = _httpContextAccessor.HttpContext.Request.Headers["Authorization"].ToString();
			reservationsClient.DefaultRequestHeaders.Authorization =
				AuthenticationHeaderValue.Parse(token);

			var reservationResponse = await reservationsClient.GetAsync(
				$"/api/reservations/{command.ReservationId}", ct);

			if (!reservationResponse.IsSuccessStatusCode)
			{
				throw new InvalidOperationException("Reservation not found or invalid");
			}

			var reservationData = await reservationResponse.Content.ReadFromJsonAsync<ReservationDto>(ct);

			if (reservationData?.Status != "Pending" && reservationData?.Status != "Held")
			{
				throw new InvalidOperationException($"Reservation is not in a payable state. Current status: {reservationData?.Status}");
			}

			// Check if payment already exists for this reservation
			var existingPayments = await _repository.GetByReservationIdAsync(command.ReservationId, ct);
			var successfulPayment = existingPayments.FirstOrDefault(p => p.Status == PaymentStatus.Succeeded);

			if (successfulPayment != null)
			{
				throw new InvalidOperationException("This reservation has already been paid");
			}

			// Create Stripe PaymentIntent
			var metadata = new Dictionary<string, string>
			{
				{ "user_id", reservationData!.UserId.ToString() },
				{ "room_id", reservationData.RoomId.ToString() },
				{ "start_date", reservationData.StartDate.ToString("yyyy-MM-dd") },
				{ "end_date", reservationData.EndDate.ToString("yyyy-MM-dd") }
			};

			var stripeResult = await _stripeService.CreatePaymentIntentAsync(
				command.Amount,
				command.Currency,
				command.ReservationId,
				metadata,
				ct);

			// Create payment record
			var payment = new Payment(command.ReservationId, command.Amount, command.Currency);
			payment.SetPaymentIntent(stripeResult.PaymentIntentId, stripeResult.ClientSecret);

			await _repository.AddAsync(payment, ct);
			await _repository.SaveChangesAsync(ct);

			_logger.LogInformation(
				"Payment intent created: PaymentId={PaymentId}, PaymentIntentId={PaymentIntentId}, ReservationId={ReservationId}",
				payment.Id, stripeResult.PaymentIntentId, command.ReservationId);

			return new PaymentIntentResponse(
				payment.Id,
				stripeResult.PaymentIntentId,
				stripeResult.ClientSecret,
				command.Amount,
				command.Currency);
		}
	}
}
