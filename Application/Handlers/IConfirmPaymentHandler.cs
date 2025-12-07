using Application.Commands;

namespace Application.Handlers;

public interface IConfirmPaymentHandler
{
    Task HandleAsync(ConfirmPaymentCommand command, CancellationToken ct = default);
}
