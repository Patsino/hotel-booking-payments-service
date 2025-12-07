using Application.Services;
using Application.StripePayments;
using Api.Controllers;
using HotelBooking.Payments.Domain.Payments;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Net;

namespace Tests.Integration;

public sealed class InternalPaymentsControllerTests
{
    private readonly Mock<IPaymentsRepository> _repositoryMock;
    private readonly Mock<IStripePaymentService> _stripeServiceMock;
    private readonly Mock<IHttpClientFactory> _httpClientFactoryMock;
    private readonly Mock<ILogger<InternalPaymentsController>> _loggerMock;
    private readonly InternalPaymentsController _controller;

    public InternalPaymentsControllerTests()
    {
        _repositoryMock = new Mock<IPaymentsRepository>();
        _stripeServiceMock = new Mock<IStripePaymentService>();
        _httpClientFactoryMock = new Mock<IHttpClientFactory>();
        _loggerMock = new Mock<ILogger<InternalPaymentsController>>();

        _controller = new InternalPaymentsController(
            _repositoryMock.Object,
            _stripeServiceMock.Object,
            _httpClientFactoryMock.Object,
            _loggerMock.Object);
    }

    #region RefundReservation Tests

    [Fact]
    public async Task RefundReservation_ShouldReturnOk_WhenRefundSucceeds()
    {
        // Arrange
        var payment = CreateSucceededPayment();

        _repositoryMock
            .Setup(x => x.GetByReservationIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Payment> { payment });

        _stripeServiceMock
            .Setup(x => x.CreateRefundAsync(
                "pi_123",
                100m,
                "requested_by_customer",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StripeRefundResult("re_123", "succeeded", 100m, null, null));

        SetupReservationsServiceMock();

        // Act
        var result = await _controller.RefundReservation(1);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        _repositoryMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RefundReservation_ShouldReturnOk_WhenRefundStatusIsPending()
    {
        // Arrange
        var payment = CreateSucceededPayment();

        _repositoryMock
            .Setup(x => x.GetByReservationIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Payment> { payment });

        _stripeServiceMock
            .Setup(x => x.CreateRefundAsync(
                It.IsAny<string>(),
                It.IsAny<decimal>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StripeRefundResult("re_123", "pending", 100m, null, null));

        SetupReservationsServiceMock();

        // Act
        var result = await _controller.RefundReservation(1);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task RefundReservation_ShouldReturnNotFound_WhenNoSuccessfulPayment()
    {
        // Arrange
        var failedPayment = new Payment(1, 100m, "EUR");
        failedPayment.MarkFailed("card_declined", "Declined");

        _repositoryMock
            .Setup(x => x.GetByReservationIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Payment> { failedPayment });

        // Act
        var result = await _controller.RefundReservation(1);

        // Assert
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task RefundReservation_ShouldReturnNotFound_WhenNoPaymentsExist()
    {
        // Arrange
        _repositoryMock
            .Setup(x => x.GetByReservationIdAsync(999, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Payment>());

        // Act
        var result = await _controller.RefundReservation(999);

        // Assert
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task RefundReservation_ShouldReturnBadRequest_WhenNoPaymentIntentId()
    {
        // Arrange
        var payment = new Payment(1, 100m, "EUR");
        payment.MarkSucceeded("pm_123"); // No SetPaymentIntent called

        _repositoryMock
            .Setup(x => x.GetByReservationIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Payment> { payment });

        // Act
        var result = await _controller.RefundReservation(1);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task RefundReservation_ShouldReturnBadRequest_WhenRefundFails()
    {
        // Arrange
        var payment = CreateSucceededPayment();

        _repositoryMock
            .Setup(x => x.GetByReservationIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Payment> { payment });

        _stripeServiceMock
            .Setup(x => x.CreateRefundAsync(
                It.IsAny<string>(),
                It.IsAny<decimal>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StripeRefundResult("", "failed", 0m, "error", "Refund failed"));

        // Act
        var result = await _controller.RefundReservation(1);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task RefundReservation_ShouldReturn500_WhenExceptionOccurs()
    {
        // Arrange
        var payment = CreateSucceededPayment();

        _repositoryMock
            .Setup(x => x.GetByReservationIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Payment> { payment });

        _stripeServiceMock
            .Setup(x => x.CreateRefundAsync(
                It.IsAny<string>(),
                It.IsAny<decimal>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Network error"));

        // Act
        var result = await _controller.RefundReservation(1);

        // Assert
        var statusResult = result.Should().BeOfType<ObjectResult>().Subject;
        statusResult.StatusCode.Should().Be(500);
    }

    #endregion

    #region GetReservationPaymentSummary Tests

    [Fact]
    public async Task GetReservationPaymentSummary_ShouldReturnSummary_WhenPaymentsExist()
    {
        // Arrange
        var payment1 = CreateSucceededPayment();
        var payment2 = new Payment(1, 50m, "EUR");
        payment2.SetPaymentIntent("pi_456", "secret");
        payment2.MarkSucceeded("pm_456");

        _repositoryMock
            .Setup(x => x.GetByReservationIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Payment> { payment1, payment2 });

        // Act
        var result = await _controller.GetReservationPaymentSummary(1);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetReservationPaymentSummary_ShouldReturnNotFound_WhenNoPayments()
    {
        // Arrange
        _repositoryMock
            .Setup(x => x.GetByReservationIdAsync(999, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Payment>());

        // Act
        var result = await _controller.GetReservationPaymentSummary(999);

        // Assert
        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task GetReservationPaymentSummary_ShouldIncludeRefundedAmount()
    {
        // Arrange
        var payment = CreateSucceededPayment();
        payment.Refund(50m);

        _repositoryMock
            .Setup(x => x.GetByReservationIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Payment> { payment });

        // Act
        var result = await _controller.GetReservationPaymentSummary(1);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
    }

    #endregion

    #region GetAllForReservation Tests

    [Fact]
    public async Task GetAllForReservation_ShouldReturnPayments()
    {
        // Arrange
        var payments = new List<Payment>
        {
            new Payment(1, 100m, "EUR"),
            new Payment(1, 50m, "EUR")
        };

        _repositoryMock
            .Setup(x => x.GetByReservationIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(payments);

        // Act
        var result = await _controller.GetAllForReservation(1);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetAllForReservation_ShouldReturnEmptyList_WhenNoPayments()
    {
        // Arrange
        _repositoryMock
            .Setup(x => x.GetByReservationIdAsync(999, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Payment>());

        // Act
        var result = await _controller.GetAllForReservation(999);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
    }

    #endregion

    #region Helper Methods

    private static Payment CreateSucceededPayment()
    {
        var payment = new Payment(1, 100m, "EUR");
        payment.SetPaymentIntent("pi_123", "secret");
        payment.MarkSucceeded("pm_123");
        return payment;
    }

    private void SetupReservationsServiceMock()
    {
        var handler = new TestHttpMessageHandler(HttpStatusCode.OK);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        _httpClientFactoryMock.Setup(x => x.CreateClient("ReservationsService")).Returns(httpClient);
    }

    private sealed class TestHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;

        public TestHttpMessageHandler(HttpStatusCode statusCode)
        {
            _statusCode = statusCode;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(_statusCode));
        }
    }

    #endregion
}
