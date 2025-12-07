using Application.Commands;

namespace Application.Handlers;

public interface IRefundPaymentHandler
{
    Task HandleAsync(RefundPaymentCommand command, CancellationToken ct = default);
}
