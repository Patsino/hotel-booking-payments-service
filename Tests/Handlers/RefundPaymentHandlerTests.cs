using Application.Commands;
using Application.Handlers;
using Application.Services;
using Application.StripePayments;
using HotelBooking.Payments.Domain.Payments;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;

namespace Tests.Handlers;

public sealed class RefundPaymentHandlerTests
{
    private readonly Mock<IPaymentsRepository> _repositoryMock;
    private readonly Mock<IStripePaymentService> _stripeServiceMock;
    private readonly Mock<IHttpClientFactory> _httpClientFactoryMock;
    private readonly Mock<ILogger<RefundPaymentHandler>> _loggerMock;
    private readonly RefundPaymentHandler _handler;

    public RefundPaymentHandlerTests()
    {
        _repositoryMock = new Mock<IPaymentsRepository>();
        _stripeServiceMock = new Mock<IStripePaymentService>();
        _httpClientFactoryMock = new Mock<IHttpClientFactory>();
        _loggerMock = new Mock<ILogger<RefundPaymentHandler>>();

        _handler = new RefundPaymentHandler(
            _repositoryMock.Object,
            _stripeServiceMock.Object,
            _httpClientFactoryMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task HandleAsync_ShouldRefundPayment_WhenPaymentSucceeded()
    {
        // Arrange
        var payment = CreateSucceededPayment();
        var command = new RefundPaymentCommand(1, 100m, "customer_request");

        _repositoryMock
            .Setup(x => x.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(payment);

        var refundResult = new StripeRefundResult("re_123", "succeeded", 100m, null, null);
        _stripeServiceMock
            .Setup(x => x.CreateRefundAsync(
                "pi_123456",
                100m,
                "customer_request",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(refundResult);

        var httpClient = CreateMockHttpClient(HttpStatusCode.OK);
        _httpClientFactoryMock.Setup(x => x.CreateClient("ReservationsService")).Returns(httpClient);

        // Act
        await _handler.HandleAsync(command);

        // Assert
        payment.Status.Should().Be(PaymentStatus.Refunded);
        payment.AmountRefunded.Should().Be(100m);
        payment.RefundedAt.Should().NotBeNull();

        _repositoryMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_ShouldRefundFullAmount_WhenNoAmountSpecified()
    {
        // Arrange
        var payment = CreateSucceededPayment();
        var command = new RefundPaymentCommand(1, null, null);

        _repositoryMock
            .Setup(x => x.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(payment);

        var refundResult = new StripeRefundResult("re_123", "succeeded", 100m, null, null);
        _stripeServiceMock
            .Setup(x => x.CreateRefundAsync(
                "pi_123456",
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(refundResult);

        var httpClient = CreateMockHttpClient(HttpStatusCode.OK);
        _httpClientFactoryMock.Setup(x => x.CreateClient("ReservationsService")).Returns(httpClient);

        // Act
        await _handler.HandleAsync(command);

        // Assert
        payment.Status.Should().Be(PaymentStatus.Refunded);
        payment.AmountRefunded.Should().Be(100m); // Full payment amount
    }

    [Fact]
    public async Task HandleAsync_ShouldNotifyReservationsService_WhenRefundSucceeds()
    {
        // Arrange
        var payment = CreateSucceededPayment();
        var command = new RefundPaymentCommand(1, 100m, "customer_request");

        _repositoryMock
            .Setup(x => x.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(payment);

        var refundResult = new StripeRefundResult("re_123", "succeeded", 100m, null, null);
        _stripeServiceMock
            .Setup(x => x.CreateRefundAsync(It.IsAny<string>(), It.IsAny<decimal?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(refundResult);

        string? requestedUrl = null;
        var httpClient = CreateMockHttpClient(HttpStatusCode.OK, captureUrl: url => requestedUrl = url);
        _httpClientFactoryMock.Setup(x => x.CreateClient("ReservationsService")).Returns(httpClient);

        // Act
        await _handler.HandleAsync(command);

        // Assert
        requestedUrl.Should().Contain($"/internal/reservations/{payment.ReservationId}/mark-canceled-refunded");
    }

    [Fact]
    public async Task HandleAsync_ShouldProcessRefund_WhenStripeReturnsPending()
    {
        // Arrange
        var payment = CreateSucceededPayment();
        var command = new RefundPaymentCommand(1, 50m, null);

        _repositoryMock
            .Setup(x => x.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(payment);

        var refundResult = new StripeRefundResult("re_123", "pending", 50m, null, null);
        _stripeServiceMock
            .Setup(x => x.CreateRefundAsync(It.IsAny<string>(), It.IsAny<decimal?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(refundResult);

        var httpClient = CreateMockHttpClient(HttpStatusCode.OK);
        _httpClientFactoryMock.Setup(x => x.CreateClient("ReservationsService")).Returns(httpClient);

        // Act
        await _handler.HandleAsync(command);

        // Assert
        payment.Status.Should().Be(PaymentStatus.Refunded);
        payment.AmountRefunded.Should().Be(50m);
    }

    [Fact]
    public async Task HandleAsync_ShouldThrowException_WhenPaymentNotFound()
    {
        // Arrange
        var command = new RefundPaymentCommand(999, 100m, null);

        _repositoryMock
            .Setup(x => x.GetByIdAsync(999, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Payment?)null);

        // Act
        var act = async () => await _handler.HandleAsync(command);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Payment not found");
    }

    [Theory]
    [InlineData(PaymentStatus.RequiresPayment)]
    [InlineData(PaymentStatus.RequiresAction)]
    [InlineData(PaymentStatus.Processing)]
    [InlineData(PaymentStatus.Failed)]
    [InlineData(PaymentStatus.Canceled)]
    [InlineData(PaymentStatus.Refunded)]
    public async Task HandleAsync_ShouldThrowException_WhenPaymentNotSucceeded(PaymentStatus status)
    {
        // Arrange
        var payment = new Payment(1, 100m, "EUR");
        payment.SetPaymentIntent("pi_123456", "secret");
        SetPaymentStatus(payment, status);

        var command = new RefundPaymentCommand(1, 100m, null);

        _repositoryMock
            .Setup(x => x.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(payment);

        // Act
        var act = async () => await _handler.HandleAsync(command);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Can only refund succeeded payments");
    }

    [Fact]
    public async Task HandleAsync_ShouldThrowException_WhenPaymentHasNoPaymentIntentId()
    {
        // Arrange
        var payment = new Payment(1, 100m, "EUR");
        payment.MarkSucceeded("pm_123");
        // Note: PaymentIntentId is null because SetPaymentIntent was not called

        var command = new RefundPaymentCommand(1, 100m, null);

        _repositoryMock
            .Setup(x => x.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(payment);

        // Act
        var act = async () => await _handler.HandleAsync(command);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Payment has no PaymentIntentId");
    }

    [Fact]
    public async Task HandleAsync_ShouldThrowException_WhenStripeRefundFails()
    {
        // Arrange
        var payment = CreateSucceededPayment();
        var command = new RefundPaymentCommand(1, 100m, null);

        _repositoryMock
            .Setup(x => x.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(payment);

        var refundResult = new StripeRefundResult("re_123", "failed", 0m, "refund_failed", "Insufficient funds for refund");
        _stripeServiceMock
            .Setup(x => x.CreateRefundAsync(It.IsAny<string>(), It.IsAny<decimal?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(refundResult);

        // Act
        var act = async () => await _handler.HandleAsync(command);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Refund failed: Insufficient funds for refund");
    }

    [Theory]
    [InlineData("customer_request")]
    [InlineData("duplicate")]
    [InlineData("fraudulent")]
    public async Task HandleAsync_ShouldPassReasonToStripe(string reason)
    {
        // Arrange
        var payment = CreateSucceededPayment();
        var command = new RefundPaymentCommand(1, 100m, reason);

        _repositoryMock
            .Setup(x => x.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(payment);

        string? capturedReason = null;
        var refundResult = new StripeRefundResult("re_123", "succeeded", 100m, null, null);
        _stripeServiceMock
            .Setup(x => x.CreateRefundAsync(It.IsAny<string>(), It.IsAny<decimal?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<string, decimal?, string?, CancellationToken>((_, _, r, _) => capturedReason = r)
            .ReturnsAsync(refundResult);

        var httpClient = CreateMockHttpClient(HttpStatusCode.OK);
        _httpClientFactoryMock.Setup(x => x.CreateClient("ReservationsService")).Returns(httpClient);

        // Act
        await _handler.HandleAsync(command);

        // Assert
        capturedReason.Should().Be(reason);
    }

    private static Payment CreateSucceededPayment()
    {
        var payment = new Payment(1, 100m, "EUR");
        payment.SetPaymentIntent("pi_123456", "secret");
        payment.MarkSucceeded("pm_123");
        return payment;
    }

    private static void SetPaymentStatus(Payment payment, PaymentStatus status)
    {
        switch (status)
        {
            case PaymentStatus.RequiresAction:
                payment.RequireAction();
                break;
            case PaymentStatus.Processing:
                payment.MarkProcessing();
                break;
            case PaymentStatus.Failed:
                payment.MarkFailed("error", "Error message");
                break;
            case PaymentStatus.Canceled:
                payment.MarkCanceled();
                break;
            case PaymentStatus.Refunded:
                payment.MarkSucceeded("pm_123");
                payment.Refund(100m);
                break;
            // RequiresPayment is default
        }
    }

    private static HttpClient CreateMockHttpClient(HttpStatusCode statusCode, object? content = null, Action<string>? captureUrl = null)
    {
        var handler = new MockHttpMessageHandler(statusCode, content, captureUrl);
        return new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost")
        };
    }

    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly object? _content;
        private readonly Action<string>? _captureUrl;

        public MockHttpMessageHandler(HttpStatusCode statusCode, object? content = null, Action<string>? captureUrl = null)
        {
            _statusCode = statusCode;
            _content = content;
            _captureUrl = captureUrl;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            _captureUrl?.Invoke(request.RequestUri?.ToString() ?? string.Empty);

            var response = new HttpResponseMessage(_statusCode);
            if (_content != null)
            {
                var json = JsonSerializer.Serialize(_content, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });
                response.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            }
            return Task.FromResult(response);
        }
    }
}
