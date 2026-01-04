using Application.StripePayments;

namespace Application.Services
{
	public interface IStripePaymentService
	{
		Task<StripePaymentIntentResult> CreatePaymentIntentAsync(
			decimal amount,
			string currency,
			int reservationId,
			Dictionary<string, string>? metadata = null,
			CancellationToken ct = default);

		Task<StripePaymentResult> ConfirmPaymentAsync(
			string paymentIntentId,
			string paymentMethodId,
			CancellationToken ct = default);

		Task<StripeRefundResult> CreateRefundAsync(
			string paymentIntentId,
			decimal? amount = null,
			string? reason = null,
			CancellationToken ct = default);

		Task<StripePaymentResult> GetPaymentIntentAsync(
			string paymentIntentId,
			CancellationToken ct = default);
	}
}
