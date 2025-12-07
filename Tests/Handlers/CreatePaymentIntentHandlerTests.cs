using Application.Commands;
using Application.Dtos;
using Application.Handlers;
using Application.Services;
using Application.StripePayments;
using HotelBooking.Payments.Domain.Payments;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Tests.Handlers;

public sealed class CreatePaymentIntentHandlerTests
{
    private readonly Mock<IPaymentsRepository> _repositoryMock;
    private readonly Mock<IStripePaymentService> _stripeServiceMock;
    private readonly Mock<IHttpClientFactory> _httpClientFactoryMock;
    private readonly Mock<ILogger<CreatePaymentIntentHandler>> _loggerMock;
    private readonly Mock<IHttpContextAccessor> _httpContextAccessorMock;
    private readonly CreatePaymentIntentHandler _handler;

    public CreatePaymentIntentHandlerTests()
    {
        _repositoryMock = new Mock<IPaymentsRepository>();
        _stripeServiceMock = new Mock<IStripePaymentService>();
        _httpClientFactoryMock = new Mock<IHttpClientFactory>();
        _loggerMock = new Mock<ILogger<CreatePaymentIntentHandler>>();
        _httpContextAccessorMock = new Mock<IHttpContextAccessor>();

        // Setup default HttpContext with Authorization header
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["Authorization"] = "Bearer test-token";
        _httpContextAccessorMock.Setup(x => x.HttpContext).Returns(httpContext);

        _handler = new CreatePaymentIntentHandler(
            _repositoryMock.Object,
            _stripeServiceMock.Object,
            _httpClientFactoryMock.Object,
            _loggerMock.Object,
            _httpContextAccessorMock.Object);
    }

    [Fact]
    public async Task HandleAsync_ShouldCreatePaymentIntent_WhenReservationIsValid()
    {
        // Arrange
        var command = new CreatePaymentIntentCommand(1, 100m, "EUR");
        var reservationDto = new ReservationDto(1, 10, 5, DateTime.Today, DateTime.Today.AddDays(2), "Pending");

        var httpClient = CreateMockHttpClient(HttpStatusCode.OK, reservationDto);
        _httpClientFactoryMock.Setup(x => x.CreateClient("ReservationsService")).Returns(httpClient);

        _repositoryMock
            .Setup(x => x.GetByReservationIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Payment>());

        var stripeResult = new StripePaymentIntentResult(
            "pi_123456",
            "pi_123456_secret_abc",
            "requires_payment_method",
            100m,
            "EUR");

        _stripeServiceMock
            .Setup(x => x.CreatePaymentIntentAsync(
                It.IsAny<decimal>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(stripeResult);

        // Act
        var result = await _handler.HandleAsync(command);

        // Assert
        result.Should().NotBeNull();
        result.PaymentIntentId.Should().Be("pi_123456");
        result.ClientSecret.Should().Be("pi_123456_secret_abc");
        result.Amount.Should().Be(100m);
        result.Currency.Should().Be("EUR");

        _repositoryMock.Verify(x => x.AddAsync(It.IsAny<Payment>(), It.IsAny<CancellationToken>()), Times.Once);
        _repositoryMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_ShouldCreatePaymentIntent_WhenReservationIsHeld()
    {
        // Arrange
        var command = new CreatePaymentIntentCommand(1, 150m, "USD");
        var reservationDto = new ReservationDto(1, 10, 5, DateTime.Today, DateTime.Today.AddDays(3), "Held");

        var httpClient = CreateMockHttpClient(HttpStatusCode.OK, reservationDto);
        _httpClientFactoryMock.Setup(x => x.CreateClient("ReservationsService")).Returns(httpClient);

        _repositoryMock
            .Setup(x => x.GetByReservationIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Payment>());

        var stripeResult = new StripePaymentIntentResult(
            "pi_789",
            "pi_789_secret",
            "requires_payment_method",
            150m,
            "USD");

        _stripeServiceMock
            .Setup(x => x.CreatePaymentIntentAsync(
                It.IsAny<decimal>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(stripeResult);

        // Act
        var result = await _handler.HandleAsync(command);

        // Assert
        result.Should().NotBeNull();
        result.Amount.Should().Be(150m);
        result.Currency.Should().Be("USD");
    }

    [Fact]
    public async Task HandleAsync_ShouldThrowException_WhenReservationNotFound()
    {
        // Arrange
        var command = new CreatePaymentIntentCommand(999, 100m, "EUR");

        var httpClient = CreateMockHttpClient(HttpStatusCode.NotFound);
        _httpClientFactoryMock.Setup(x => x.CreateClient("ReservationsService")).Returns(httpClient);

        // Act
        var act = async () => await _handler.HandleAsync(command);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Reservation not found or invalid");
    }

    [Theory]
    [InlineData("Confirmed")]
    [InlineData("Canceled")]
    [InlineData("Completed")]
    public async Task HandleAsync_ShouldThrowException_WhenReservationStatusIsNotPayable(string status)
    {
        // Arrange
        var command = new CreatePaymentIntentCommand(1, 100m, "EUR");
        var reservationDto = new ReservationDto(1, 10, 5, DateTime.Today, DateTime.Today.AddDays(2), status);

        var httpClient = CreateMockHttpClient(HttpStatusCode.OK, reservationDto);
        _httpClientFactoryMock.Setup(x => x.CreateClient("ReservationsService")).Returns(httpClient);

        // Act
        var act = async () => await _handler.HandleAsync(command);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"Reservation is not in a payable state. Current status: {status}");
    }

    [Fact]
    public async Task HandleAsync_ShouldThrowException_WhenReservationAlreadyPaid()
    {
        // Arrange
        var command = new CreatePaymentIntentCommand(1, 100m, "EUR");
        var reservationDto = new ReservationDto(1, 10, 5, DateTime.Today, DateTime.Today.AddDays(2), "Pending");

        var httpClient = CreateMockHttpClient(HttpStatusCode.OK, reservationDto);
        _httpClientFactoryMock.Setup(x => x.CreateClient("ReservationsService")).Returns(httpClient);

        var existingPayment = new Payment(1, 100m, "EUR");
        existingPayment.MarkSucceeded("pm_existing");

        _repositoryMock
            .Setup(x => x.GetByReservationIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Payment> { existingPayment });

        // Act
        var act = async () => await _handler.HandleAsync(command);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("This reservation has already been paid");
    }

    [Fact]
    public async Task HandleAsync_ShouldAllowNewPayment_WhenExistingPaymentFailed()
    {
        // Arrange
        var command = new CreatePaymentIntentCommand(1, 100m, "EUR");
        var reservationDto = new ReservationDto(1, 10, 5, DateTime.Today, DateTime.Today.AddDays(2), "Pending");

        var httpClient = CreateMockHttpClient(HttpStatusCode.OK, reservationDto);
        _httpClientFactoryMock.Setup(x => x.CreateClient("ReservationsService")).Returns(httpClient);

        var failedPayment = new Payment(1, 100m, "EUR");
        failedPayment.MarkFailed("card_declined", "Card was declined");

        _repositoryMock
            .Setup(x => x.GetByReservationIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Payment> { failedPayment });

        var stripeResult = new StripePaymentIntentResult(
            "pi_new",
            "pi_new_secret",
            "requires_payment_method",
            100m,
            "EUR");

        _stripeServiceMock
            .Setup(x => x.CreatePaymentIntentAsync(
                It.IsAny<decimal>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(stripeResult);

        // Act
        var result = await _handler.HandleAsync(command);

        // Assert
        result.Should().NotBeNull();
        result.PaymentIntentId.Should().Be("pi_new");
    }

    [Fact]
    public async Task HandleAsync_ShouldPassCorrectMetadataToStripe()
    {
        // Arrange
        var command = new CreatePaymentIntentCommand(1, 100m, "EUR");
        var reservationDto = new ReservationDto(1, 10, 5, DateTime.Today, DateTime.Today.AddDays(2), "Pending");

        var httpClient = CreateMockHttpClient(HttpStatusCode.OK, reservationDto);
        _httpClientFactoryMock.Setup(x => x.CreateClient("ReservationsService")).Returns(httpClient);

        _repositoryMock
            .Setup(x => x.GetByReservationIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Payment>());

        Dictionary<string, string>? capturedMetadata = null;
        _stripeServiceMock
            .Setup(x => x.CreatePaymentIntentAsync(
                It.IsAny<decimal>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .Callback<decimal, string, int, Dictionary<string, string>?, CancellationToken>(
                (_, _, _, metadata, _) => capturedMetadata = metadata)
            .ReturnsAsync(new StripePaymentIntentResult("pi_123", "secret", "requires_payment_method", 100m, "EUR"));

        // Act
        await _handler.HandleAsync(command);

        // Assert
        capturedMetadata.Should().NotBeNull();
        capturedMetadata.Should().ContainKey("user_id");
        capturedMetadata.Should().ContainKey("room_id");
        capturedMetadata.Should().ContainKey("start_date");
        capturedMetadata.Should().ContainKey("end_date");
    }

    private static HttpClient CreateMockHttpClient(HttpStatusCode statusCode, object? content = null)
    {
        var handler = new MockHttpMessageHandler(statusCode, content);
        return new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost")
        };
    }

    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly object? _content;

        public MockHttpMessageHandler(HttpStatusCode statusCode, object? content = null)
        {
            _statusCode = statusCode;
            _content = content;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
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
