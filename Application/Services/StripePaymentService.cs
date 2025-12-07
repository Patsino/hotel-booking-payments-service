using Application.StripePayments;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Stripe;
using System.Diagnostics.CodeAnalysis;

namespace Application.Services
{
	[ExcludeFromCodeCoverage]
	public sealed class StripePaymentService : IStripePaymentService
	{
		private readonly ILogger<StripePaymentService> _logger;
		private readonly string _apiKey;

		public StripePaymentService(IConfiguration configuration, ILogger<StripePaymentService> logger)
		{
			_logger = logger;
			
			// Support both environment variables and appsettings
			_apiKey = Environment.GetEnvironmentVariable("Stripe__SecretKey")
				?? configuration["Stripe:SecretKey"]
				?? throw new InvalidOperationException("Stripe:SecretKey not configured");

			StripeConfiguration.ApiKey = _apiKey;
		}

		public async Task<StripePaymentIntentResult> CreatePaymentIntentAsync(
			decimal amount,
			string currency,
			int reservationId,
			Dictionary<string, string>? metadata = null,
			CancellationToken ct = default)
		{
			try
			{
				var service = new PaymentIntentService();

				// Convert amount to cents (Stripe requires smallest currency unit)
				var amountInCents = (long)(amount * 100);

				var options = new PaymentIntentCreateOptions
				{
					Amount = amountInCents,
					Currency = currency.ToLower(),
					AutomaticPaymentMethods = new PaymentIntentAutomaticPaymentMethodsOptions
					{
						Enabled = true,
					},
					Metadata = metadata ?? new Dictionary<string, string>(),
					Description = $"Reservation #{reservationId}",
					StatementDescriptorSuffix = "HOTEL BOOKING",
					CaptureMethod = "automatic", // Charge immediately when customer confirms
				};

				// Add reservation ID to metadata for tracking
				options.Metadata["reservation_id"] = reservationId.ToString();
				options.Metadata["created_at"] = DateTimeOffset.UtcNow.ToString("o");

				var intent = await service.CreateAsync(options, cancellationToken: ct);

				_logger.LogInformation(
					"Stripe PaymentIntent created: {PaymentIntentId} for Reservation {ReservationId}, Amount: {Amount} {Currency}",
					intent.Id, reservationId, amount, currency);

				return new StripePaymentIntentResult(
					intent.Id,
					intent.ClientSecret,
					intent.Status,
					amount,
					currency.ToUpper());
			}
			catch (StripeException ex)
			{
				_logger.LogError(ex, "Stripe error creating payment intent: {Error}", ex.Message);
				throw new InvalidOperationException($"Payment provider error: {ex.StripeError?.Message ?? ex.Message}", ex);
			}
		}

		public async Task<StripePaymentResult> ConfirmPaymentAsync(
			string paymentIntentId,
			string paymentMethodId,
			CancellationToken ct = default)
		{
			try
			{
				var service = new PaymentIntentService();

				var options = new PaymentIntentConfirmOptions
				{
					PaymentMethod = paymentMethodId,
					ReturnUrl = "https://your-app.com/payment-success", // Configure this
				};

				var intent = await service.ConfirmAsync(paymentIntentId, options, cancellationToken: ct);

				_logger.LogInformation(
					"Stripe PaymentIntent confirmed: {PaymentIntentId}, Status: {Status}",
					intent.Id, intent.Status);

				return new StripePaymentResult(
					intent.Id,
					intent.Status,
					intent.PaymentMethodId,
					intent.LastPaymentError?.Code,
					intent.LastPaymentError?.Message);
			}
			catch (StripeException ex)
			{
				_logger.LogError(ex, "Stripe error confirming payment: {Error}", ex.Message);

				return new StripePaymentResult(
					paymentIntentId,
					"failed",
					null,
					ex.StripeError?.Code,
					ex.StripeError?.Message ?? ex.Message);
			}
		}

		public async Task<StripeRefundResult> CreateRefundAsync(
			string paymentIntentId,
			decimal? amount = null,
			string? reason = null,
			CancellationToken ct = default)
		{
			try
			{
				var service = new RefundService();

				var options = new RefundCreateOptions
				{
					PaymentIntent = paymentIntentId,
					Reason = reason switch
					{
						"duplicate" => "duplicate",
						"fraudulent" => "fraudulent",
						_ => "requested_by_customer"
					}
				};

				// If partial refund
				if (amount.HasValue)
				{
					options.Amount = (long)(amount.Value * 100);
				}

				var refund = await service.CreateAsync(options, cancellationToken: ct);

				_logger.LogInformation(
					"Stripe Refund created: {RefundId} for PaymentIntent {PaymentIntentId}, Amount: {Amount}, Status: {Status}",
					refund.Id, paymentIntentId, refund.Amount / 100m, refund.Status);

				return new StripeRefundResult(
					refund.Id,
					refund.Status,
					refund.Amount / 100m,
					null,
					null);
			}
			catch (StripeException ex)
			{
				_logger.LogError(ex, "Stripe error creating refund: {Error}", ex.Message);

				return new StripeRefundResult(
					string.Empty,
					"failed",
					0,
					ex.StripeError?.Code,
					ex.StripeError?.Message ?? ex.Message);
			}
		}

		public async Task<StripePaymentResult> GetPaymentIntentAsync(
			string paymentIntentId,
			CancellationToken ct = default)
		{
			try
			{
				var service = new PaymentIntentService();
				var intent = await service.GetAsync(paymentIntentId, cancellationToken: ct);

				return new StripePaymentResult(
					intent.Id,
					intent.Status,
					intent.PaymentMethodId,
					intent.LastPaymentError?.Code,
					intent.LastPaymentError?.Message);
			}
			catch (StripeException ex)
			{
				_logger.LogError(ex, "Stripe error getting payment intent: {Error}", ex.Message);
				throw new InvalidOperationException($"Payment provider error: {ex.StripeError?.Message ?? ex.Message}", ex);
			}
		}
	}
}
