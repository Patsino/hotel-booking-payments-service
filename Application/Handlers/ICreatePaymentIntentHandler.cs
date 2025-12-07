using Application.Commands;
using Application.Dtos;

namespace Application.Handlers;

public interface ICreatePaymentIntentHandler
{
    Task<PaymentIntentResponse> HandleAsync(CreatePaymentIntentCommand command, CancellationToken ct = default);
}
