using Application.Commands;
using Application.Dtos;
using Application.Handlers;
using Application.Services;
using Api.Controllers;
using HotelBooking.Payments.Domain.Payments;
using Infrastructure.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Security.Claims;
using System.Text.Json;

namespace Tests.Integration;

public sealed class PaymentsControllerTests
{
    private readonly Mock<IPaymentsRepository> _repositoryMock;
    private readonly Mock<ICreatePaymentIntentHandler> _createIntentHandlerMock;
    private readonly Mock<IConfirmPaymentHandler> _confirmHandlerMock;
    private readonly Mock<IRefundPaymentHandler> _refundHandlerMock;
    private readonly Mock<IHttpClientFactory> _httpClientFactoryMock;
    private readonly Mock<ICurrentUserService> _currentUserMock;
    private readonly Mock<ILogger<PaymentsController>> _loggerMock;
    private readonly PaymentsController _controller;

    public PaymentsControllerTests()
    {
        _repositoryMock = new Mock<IPaymentsRepository>();
        _createIntentHandlerMock = new Mock<ICreatePaymentIntentHandler>();
        _confirmHandlerMock = new Mock<IConfirmPaymentHandler>();
        _refundHandlerMock = new Mock<IRefundPaymentHandler>();
        _httpClientFactoryMock = new Mock<IHttpClientFactory>();
        _currentUserMock = new Mock<ICurrentUserService>();
        _loggerMock = new Mock<ILogger<PaymentsController>>();

        _controller = new PaymentsController(
            _repositoryMock.Object,
            _createIntentHandlerMock.Object,
            _confirmHandlerMock.Object,
            _refundHandlerMock.Object,
            _httpClientFactoryMock.Object,
            _currentUserMock.Object,
            _loggerMock.Object);

        // Setup default user
        _currentUserMock.Setup(x => x.UserId).Returns(1);
        _currentUserMock.Setup(x => x.IsAdmin).Returns(false);
    }

    #region GetById Tests

    [Fact]
    public async Task GetById_ShouldReturnPayment_WhenExists()
    {
        // Arrange
        var payment = new Payment(1, 100m, "EUR");
        payment.SetPaymentIntent("pi_123", "secret");
        
        _repositoryMock
            .Setup(x => x.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(payment);

        SetupReservationsServiceMock(1, 1);

        // Act
        var result = await _controller.GetById(1);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetById_ShouldReturnNotFound_WhenPaymentNotExists()
    {
        // Arrange
        _repositoryMock
            .Setup(x => x.GetByIdAsync(999, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Payment?)null);

        // Act
        var result = await _controller.GetById(999);

        // Assert
        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task GetById_ShouldReturnForbidden_WhenUserDoesNotOwnReservation()
    {
        // Arrange
        var payment = new Payment(1, 100m, "EUR");
        
        _repositoryMock
            .Setup(x => x.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(payment);

        // Different user owns the reservation
        SetupReservationsServiceMock(1, 999);
        _currentUserMock.Setup(x => x.UserId).Returns(1);
        _currentUserMock.Setup(x => x.IsAdmin).Returns(false);

        // Act
        var result = await _controller.GetById(1);

        // Assert
        result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task GetById_ShouldReturnPayment_WhenAdminAccessesAnyPayment()
    {
        // Arrange
        var payment = new Payment(1, 100m, "EUR");
        
        _repositoryMock
            .Setup(x => x.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(payment);

        SetupReservationsServiceMock(1, 999); // Different user
        _currentUserMock.Setup(x => x.IsAdmin).Returns(true);

        // Act
        var result = await _controller.GetById(1);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetById_ShouldReturnPayment_WhenReservationServiceFails()
    {
        // Arrange - When reservation service is down, we still return payment for owner check
        var payment = new Payment(1, 100m, "EUR");

        _repositoryMock
            .Setup(x => x.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(payment);

        // Reservation service returns error
        SetupReservationsServiceMock(1, 1, HttpStatusCode.InternalServerError);

        // Act
        var result = await _controller.GetById(1);

        // Assert - Should still return ok because reservation check failed but we don't forbid
        result.Should().BeOfType<OkObjectResult>();
    }

    #endregion

    #region GetByReservation Tests

    [Fact]
    public async Task GetByReservation_ShouldReturnPayments_WhenUserOwnsReservation()
    {
        // Arrange
        var payments = new List<Payment>
        {
            new Payment(1, 100m, "EUR"),
            new Payment(1, 150m, "EUR")
        };
        
        _repositoryMock
            .Setup(x => x.GetByReservationIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(payments);

        SetupReservationsServiceMock(1, 1);

        // Act
        var result = await _controller.GetByReservation(1);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetByReservation_ShouldReturnPayments_WhenAdminAccessesAnyReservation()
    {
        // Arrange
        var payments = new List<Payment>
        {
            new Payment(1, 100m, "EUR")
        };

        _repositoryMock
            .Setup(x => x.GetByReservationIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(payments);

        SetupReservationsServiceMock(1, 999); // Different user
        _currentUserMock.Setup(x => x.IsAdmin).Returns(true);

        // Act
        var result = await _controller.GetByReservation(1);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetByReservation_ShouldReturnNotFound_WhenReservationNotFound()
    {
        // Arrange
        SetupReservationsServiceMock(1, 1, HttpStatusCode.NotFound);

        // Act
        var result = await _controller.GetByReservation(999);

        // Assert
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task GetByReservation_ShouldReturnForbidden_WhenUserDoesNotOwnReservation()
    {
        // Arrange
        SetupReservationsServiceMock(1, 999);
        _currentUserMock.Setup(x => x.UserId).Returns(1);
        _currentUserMock.Setup(x => x.IsAdmin).Returns(false);

        // Act
        var result = await _controller.GetByReservation(1);

        // Assert
        result.Should().BeOfType<ForbidResult>();
    }

    #endregion

    #region CreatePaymentIntent Tests

    [Fact]
    public async Task CreatePaymentIntent_ShouldReturnOk_WhenValidRequest()
    {
        // Arrange
        var command = new CreatePaymentIntentCommand(1, 100m, "EUR");
        var response = new PaymentIntentResponse(1, "pi_123", "secret", 100m, "EUR");

        SetupReservationsServiceMock(1, 1);

        _createIntentHandlerMock
            .Setup(x => x.HandleAsync(It.IsAny<CreatePaymentIntentCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _controller.CreatePaymentIntent(command);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task CreatePaymentIntent_ShouldReturnOk_WhenAdminAccessesAnyReservation()
    {
        // Arrange
        var command = new CreatePaymentIntentCommand(1, 100m, "EUR");
        var response = new PaymentIntentResponse(1, "pi_123", "secret", 100m, "EUR");

        SetupReservationsServiceMock(1, 999); // Different user owns reservation
        _currentUserMock.Setup(x => x.UserId).Returns(1);
        _currentUserMock.Setup(x => x.IsAdmin).Returns(true); // But current user is admin

        _createIntentHandlerMock
            .Setup(x => x.HandleAsync(It.IsAny<CreatePaymentIntentCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _controller.CreatePaymentIntent(command);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task CreatePaymentIntent_ShouldReturnBadRequest_WhenReservationNotFound()
    {
        // Arrange
        var command = new CreatePaymentIntentCommand(999, 100m, "EUR");
        SetupReservationsServiceMock(999, 1, HttpStatusCode.NotFound);

        // Act
        var result = await _controller.CreatePaymentIntent(command);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task CreatePaymentIntent_ShouldReturnForbidden_WhenUserDoesNotOwnReservation()
    {
        // Arrange
        var command = new CreatePaymentIntentCommand(1, 100m, "EUR");
        SetupReservationsServiceMock(1, 999);
        _currentUserMock.Setup(x => x.UserId).Returns(1);
        _currentUserMock.Setup(x => x.IsAdmin).Returns(false);

        // Act
        var result = await _controller.CreatePaymentIntent(command);

        // Assert
        result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task CreatePaymentIntent_ShouldReturnBadRequest_WhenHandlerThrows()
    {
        // Arrange
        var command = new CreatePaymentIntentCommand(1, 100m, "EUR");
        SetupReservationsServiceMock(1, 1);

        _createIntentHandlerMock
            .Setup(x => x.HandleAsync(It.IsAny<CreatePaymentIntentCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Reservation already paid"));

        // Act
        var result = await _controller.CreatePaymentIntent(command);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    #endregion

    #region ConfirmPayment Tests

    [Fact]
    public async Task ConfirmPayment_ShouldReturnOk_WhenValidRequest()
    {
        // Arrange
        var command = new ConfirmPaymentCommand("pi_123", "pm_456");
        var payment = new Payment(1, 100m, "EUR");
        payment.SetPaymentIntent("pi_123", "secret");

        _repositoryMock
            .Setup(x => x.GetByPaymentIntentIdAsync("pi_123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Payment> { payment });

        SetupReservationsServiceMock(1, 1);

        _confirmHandlerMock
            .Setup(x => x.HandleAsync(It.IsAny<ConfirmPaymentCommand>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _controller.ConfirmPayment(command);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task ConfirmPayment_ShouldReturnOk_WhenAdminConfirmsPayment()
    {
        // Arrange
        var command = new ConfirmPaymentCommand("pi_123", "pm_456");
        var payment = new Payment(1, 100m, "EUR");
        payment.SetPaymentIntent("pi_123", "secret");

        _repositoryMock
            .Setup(x => x.GetByPaymentIntentIdAsync("pi_123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Payment> { payment });

        SetupReservationsServiceMock(1, 999); // Different user owns reservation
        _currentUserMock.Setup(x => x.UserId).Returns(1);
        _currentUserMock.Setup(x => x.IsAdmin).Returns(true);

        _confirmHandlerMock
            .Setup(x => x.HandleAsync(It.IsAny<ConfirmPaymentCommand>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _controller.ConfirmPayment(command);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task ConfirmPayment_ShouldReturnBadRequest_WhenPaymentNotFound()
    {
        // Arrange
        var command = new ConfirmPaymentCommand("pi_nonexistent", "pm_456");

        _repositoryMock
            .Setup(x => x.GetByPaymentIntentIdAsync("pi_nonexistent", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Payment>());

        // Act
        var result = await _controller.ConfirmPayment(command);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task ConfirmPayment_ShouldReturnBadRequest_WhenReservationNotFound()
    {
        // Arrange
        var command = new ConfirmPaymentCommand("pi_123", "pm_456");
        var payment = new Payment(1, 100m, "EUR");
        payment.SetPaymentIntent("pi_123", "secret");

        _repositoryMock
            .Setup(x => x.GetByPaymentIntentIdAsync("pi_123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Payment> { payment });

        SetupReservationsServiceMock(1, 1, HttpStatusCode.NotFound);

        // Act
        var result = await _controller.ConfirmPayment(command);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task ConfirmPayment_ShouldReturnForbidden_WhenUserDoesNotOwnReservation()
    {
        // Arrange
        var command = new ConfirmPaymentCommand("pi_123", "pm_456");
        var payment = new Payment(1, 100m, "EUR");
        payment.SetPaymentIntent("pi_123", "secret");

        _repositoryMock
            .Setup(x => x.GetByPaymentIntentIdAsync("pi_123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Payment> { payment });

        SetupReservationsServiceMock(1, 999);
        _currentUserMock.Setup(x => x.UserId).Returns(1);
        _currentUserMock.Setup(x => x.IsAdmin).Returns(false);

        // Act
        var result = await _controller.ConfirmPayment(command);

        // Assert
        result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task ConfirmPayment_ShouldReturnBadRequest_WhenHandlerThrows()
    {
        // Arrange
        var command = new ConfirmPaymentCommand("pi_123", "pm_456");
        var payment = new Payment(1, 100m, "EUR");
        payment.SetPaymentIntent("pi_123", "secret");

        _repositoryMock
            .Setup(x => x.GetByPaymentIntentIdAsync("pi_123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Payment> { payment });

        SetupReservationsServiceMock(1, 1);

        _confirmHandlerMock
            .Setup(x => x.HandleAsync(It.IsAny<ConfirmPaymentCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Stripe error"));

        // Act
        var result = await _controller.ConfirmPayment(command);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    #endregion

    #region RefundPayment Tests

    [Fact]
    public async Task RefundPayment_ShouldReturnOk_WhenValidRefundAsAdmin()
    {
        // Arrange - Admin user
        _currentUserMock.Setup(x => x.IsAdmin).Returns(true);

        var command = new RefundPaymentCommand(1, 100m, "customer_request");

        _refundHandlerMock
            .Setup(x => x.HandleAsync(It.IsAny<RefundPaymentCommand>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Note: RefundPayment has AdminOnly policy - in unit test we bypass auth
        // Act
        var result = await _controller.RefundPayment(1, command);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task RefundPayment_ShouldReturnBadRequest_WhenHandlerThrows()
    {
        // Arrange
        _currentUserMock.Setup(x => x.IsAdmin).Returns(true);
        var command = new RefundPaymentCommand(1, 100m, null);

        _refundHandlerMock
            .Setup(x => x.HandleAsync(It.IsAny<RefundPaymentCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Payment not found"));

        // Act
        var result = await _controller.RefundPayment(1, command);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    #endregion

    #region Helper Methods

    private void SetupReservationsServiceMock(int reservationId, int userId, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        var handler = new TestHttpMessageHandler(statusCode, new ReservationDto(
            reservationId, userId, 1, DateTime.Today, DateTime.Today.AddDays(2), "Pending"));

        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        _httpClientFactoryMock.Setup(x => x.CreateClient("ReservationsService")).Returns(httpClient);
    }

    private sealed class TestHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly object? _content;

        public TestHttpMessageHandler(HttpStatusCode statusCode, object? content = null)
        {
            _statusCode = statusCode;
            _content = content;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(_statusCode);
            if (_content != null && _statusCode == HttpStatusCode.OK)
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

    #endregion
}
