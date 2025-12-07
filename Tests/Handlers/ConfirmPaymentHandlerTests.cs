using Application.Commands;
using Application.Handlers;
using Application.Services;
using Application.StripePayments;
using HotelBooking.Payments.Domain.Payments;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;

namespace Tests.Handlers;

public sealed class ConfirmPaymentHandlerTests
{
    private readonly Mock<IPaymentsRepository> _repositoryMock;
    private readonly Mock<IStripePaymentService> _stripeServiceMock;
    private readonly Mock<IHttpClientFactory> _httpClientFactoryMock;
    private readonly Mock<ILogger<ConfirmPaymentHandler>> _loggerMock;
    private readonly ConfirmPaymentHandler _handler;

    public ConfirmPaymentHandlerTests()
    {
        _repositoryMock = new Mock<IPaymentsRepository>();
        _stripeServiceMock = new Mock<IStripePaymentService>();
        _httpClientFactoryMock = new Mock<IHttpClientFactory>();
        _loggerMock = new Mock<ILogger<ConfirmPaymentHandler>>();

        _handler = new ConfirmPaymentHandler(
            _repositoryMock.Object,
            _stripeServiceMock.Object,
            _httpClientFactoryMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task HandleAsync_ShouldConfirmPaymentSuccessfully_WhenStripeReturnsSucceeded()
    {
        // Arrange
        var command = new ConfirmPaymentCommand("pi_123456", "pm_123456");
        var payment = new Payment(1, 100m, "EUR");
        payment.SetPaymentIntent("pi_123456", "secret");

        _repositoryMock
            .Setup(x => x.GetByPaymentIntentIdAsync("pi_123456", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Payment> { payment });

        var stripeResult = new StripePaymentResult(
            "pi_123456",
            "succeeded",
            "pm_123456",
            null,
            null);

        _stripeServiceMock
            .Setup(x => x.ConfirmPaymentAsync("pi_123456", "pm_123456", It.IsAny<CancellationToken>()))
            .ReturnsAsync(stripeResult);

        var httpClient = CreateMockHttpClient(HttpStatusCode.OK);
        _httpClientFactoryMock.Setup(x => x.CreateClient("ReservationsService")).Returns(httpClient);

        // Act
        await _handler.HandleAsync(command);

        // Assert
        payment.Status.Should().Be(PaymentStatus.Succeeded);
        payment.ProviderPaymentId.Should().Be("pm_123456");
        payment.PaidAt.Should().NotBeNull();

        _repositoryMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_ShouldNotifyReservationsService_WhenPaymentSucceeds()
    {
        // Arrange
        var command = new ConfirmPaymentCommand("pi_123456", "pm_123456");
        var payment = new Payment(1, 100m, "EUR");
        payment.SetPaymentIntent("pi_123456", "secret");

        _repositoryMock
            .Setup(x => x.GetByPaymentIntentIdAsync("pi_123456", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Payment> { payment });

        var stripeResult = new StripePaymentResult("pi_123456", "succeeded", "pm_123456", null, null);
        _stripeServiceMock
            .Setup(x => x.ConfirmPaymentAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(stripeResult);

        string? requestedUrl = null;
        var httpClient = CreateMockHttpClient(HttpStatusCode.OK, captureUrl: url => requestedUrl = url);
        _httpClientFactoryMock.Setup(x => x.CreateClient("ReservationsService")).Returns(httpClient);

        // Act
        await _handler.HandleAsync(command);

        // Assert
        requestedUrl.Should().Contain($"/internal/reservations/{payment.ReservationId}/mark-confirmed");
    }

    [Fact]
    public async Task HandleAsync_ShouldSetRequiresAction_WhenStripeReturnsRequiresAction()
    {
        // Arrange
        var command = new ConfirmPaymentCommand("pi_123456", "pm_123456");
        var payment = new Payment(1, 100m, "EUR");
        payment.SetPaymentIntent("pi_123456", "secret");

        _repositoryMock
            .Setup(x => x.GetByPaymentIntentIdAsync("pi_123456", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Payment> { payment });

        var stripeResult = new StripePaymentResult(
            "pi_123456",
            "requires_action",
            null,
            null,
            null);

        _stripeServiceMock
            .Setup(x => x.ConfirmPaymentAsync("pi_123456", "pm_123456", It.IsAny<CancellationToken>()))
            .ReturnsAsync(stripeResult);

        // Act
        await _handler.HandleAsync(command);

        // Assert
        payment.Status.Should().Be(PaymentStatus.RequiresAction);
        _repositoryMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_ShouldMarkFailed_WhenStripeReturnsFailure()
    {
        // Arrange
        var command = new ConfirmPaymentCommand("pi_123456", "pm_123456");
        var payment = new Payment(1, 100m, "EUR");
        payment.SetPaymentIntent("pi_123456", "secret");

        _repositoryMock
            .Setup(x => x.GetByPaymentIntentIdAsync("pi_123456", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Payment> { payment });

        var stripeResult = new StripePaymentResult(
            "pi_123456",
            "failed",
            null,
            "card_declined",
            "Your card was declined");

        _stripeServiceMock
            .Setup(x => x.ConfirmPaymentAsync("pi_123456", "pm_123456", It.IsAny<CancellationToken>()))
            .ReturnsAsync(stripeResult);

        // Act
        await _handler.HandleAsync(command);

        // Assert
        payment.Status.Should().Be(PaymentStatus.Failed);
        payment.ErrorCode.Should().Be("card_declined");
        payment.ErrorMessage.Should().Be("Your card was declined");
        payment.IsActive.Should().BeFalse();

        _repositoryMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_ShouldThrowException_WhenPaymentNotFound()
    {
        // Arrange
        var command = new ConfirmPaymentCommand("pi_nonexistent", "pm_123456");

        _repositoryMock
            .Setup(x => x.GetByPaymentIntentIdAsync("pi_nonexistent", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Payment>());

        // Act
        var act = async () => await _handler.HandleAsync(command);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Payment not found");
    }

    [Theory]
    [InlineData("processing")]
    [InlineData("requires_payment_method")]
    [InlineData("canceled")]
    public async Task HandleAsync_ShouldMarkFailed_WhenStripeReturnsUnexpectedStatus(string status)
    {
        // Arrange
        var command = new ConfirmPaymentCommand("pi_123456", "pm_123456");
        var payment = new Payment(1, 100m, "EUR");
        payment.SetPaymentIntent("pi_123456", "secret");

        _repositoryMock
            .Setup(x => x.GetByPaymentIntentIdAsync("pi_123456", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Payment> { payment });

        var stripeResult = new StripePaymentResult("pi_123456", status, null, null, null);
        _stripeServiceMock
            .Setup(x => x.ConfirmPaymentAsync("pi_123456", "pm_123456", It.IsAny<CancellationToken>()))
            .ReturnsAsync(stripeResult);

        // Act
        await _handler.HandleAsync(command);

        // Assert
        payment.Status.Should().Be(PaymentStatus.Failed);
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
