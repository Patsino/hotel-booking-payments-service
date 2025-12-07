namespace Tests.Domain;

public sealed class PaymentTests
{
    #region Constructor Tests

    [Fact]
    public void Constructor_ShouldInitializePayment_WithValidParameters()
    {
        // Arrange
        var reservationId = 1;
        var amount = 100.50m;
        var currency = "EUR";

        // Act
        var payment = new Payment(reservationId, amount, currency);

        // Assert
        payment.ReservationId.Should().Be(reservationId);
        payment.Amount.Should().Be(amount);
        payment.Currency.Should().Be(currency);
        payment.Status.Should().Be(PaymentStatus.RequiresPayment);
        payment.Provider.Should().Be(PaymentProvider.Stripe);
        payment.IsActive.Should().BeTrue();
        payment.AmountRefunded.Should().Be(0);
        payment.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Theory]
    [InlineData(0.01)]
    [InlineData(100.00)]
    [InlineData(9999.99)]
    public void Constructor_ShouldAcceptVariousAmounts(decimal amount)
    {
        // Arrange & Act
        var payment = new Payment(1, amount, "EUR");

        // Assert
        payment.Amount.Should().Be(amount);
    }

    [Theory]
    [InlineData("EUR")]
    [InlineData("USD")]
    [InlineData("GBP")]
    public void Constructor_ShouldAcceptVariousCurrencies(string currency)
    {
        // Arrange & Act
        var payment = new Payment(1, 100m, currency);

        // Assert
        payment.Currency.Should().Be(currency);
    }

    #endregion

    #region SetPaymentIntent Tests

    [Fact]
    public void SetPaymentIntent_ShouldSetPaymentIntentIdAndStatus()
    {
        // Arrange
        var payment = new Payment(1, 100m, "EUR");
        var paymentIntentId = "pi_123456";
        var clientSecret = "pi_123456_secret_abc";

        // Act
        payment.SetPaymentIntent(paymentIntentId, clientSecret);

        // Assert
        payment.PaymentIntentId.Should().Be(paymentIntentId);
        payment.Status.Should().Be(PaymentStatus.RequiresPayment);
    }

    #endregion

    #region MarkSucceeded Tests

    [Fact]
    public void MarkSucceeded_ShouldUpdateStatusAndProviderPaymentId()
    {
        // Arrange
        var payment = new Payment(1, 100m, "EUR");
        payment.SetPaymentIntent("pi_123", "secret");
        var providerPaymentId = "pm_123456";

        // Act
        payment.MarkSucceeded(providerPaymentId);

        // Assert
        payment.Status.Should().Be(PaymentStatus.Succeeded);
        payment.ProviderPaymentId.Should().Be(providerPaymentId);
        payment.PaidAt.Should().NotBeNull();
        payment.PaidAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
        payment.ErrorCode.Should().BeNull();
        payment.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void MarkSucceeded_ShouldClearPreviousErrors()
    {
        // Arrange
        var payment = new Payment(1, 100m, "EUR");
        payment.MarkFailed("error_code", "Error message");

        // Act
        payment.MarkSucceeded("pm_123");

        // Assert
        payment.Status.Should().Be(PaymentStatus.Succeeded);
        payment.ErrorCode.Should().BeNull();
        payment.ErrorMessage.Should().BeNull();
    }

    #endregion

    #region MarkFailed Tests

    [Fact]
    public void MarkFailed_ShouldUpdateStatusAndErrorDetails()
    {
        // Arrange
        var payment = new Payment(1, 100m, "EUR");
        var errorCode = "card_declined";
        var errorMessage = "Your card was declined";

        // Act
        payment.MarkFailed(errorCode, errorMessage);

        // Assert
        payment.Status.Should().Be(PaymentStatus.Failed);
        payment.ErrorCode.Should().Be(errorCode);
        payment.ErrorMessage.Should().Be(errorMessage);
        payment.IsActive.Should().BeFalse();
    }

    [Fact]
    public void MarkFailed_ShouldWorkWithNullErrors()
    {
        // Arrange
        var payment = new Payment(1, 100m, "EUR");

        // Act
        payment.MarkFailed(null, null);

        // Assert
        payment.Status.Should().Be(PaymentStatus.Failed);
        payment.ErrorCode.Should().BeNull();
        payment.ErrorMessage.Should().BeNull();
        payment.IsActive.Should().BeFalse();
    }

    #endregion

    #region Refund Tests

    [Fact]
    public void Refund_ShouldUpdateStatusAndRefundAmount()
    {
        // Arrange
        var payment = new Payment(1, 100m, "EUR");
        payment.MarkSucceeded("pm_123");
        var refundAmount = 50m;

        // Act
        payment.Refund(refundAmount);

        // Assert
        payment.Status.Should().Be(PaymentStatus.Refunded);
        payment.AmountRefunded.Should().Be(refundAmount);
        payment.RefundedAt.Should().NotBeNull();
        payment.RefundedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Refund_ShouldAllowFullRefund()
    {
        // Arrange
        var payment = new Payment(1, 100m, "EUR");
        payment.MarkSucceeded("pm_123");

        // Act
        payment.Refund(100m);

        // Assert
        payment.AmountRefunded.Should().Be(100m);
        payment.Status.Should().Be(PaymentStatus.Refunded);
    }

    [Fact]
    public void Refund_ShouldThrowException_WhenRefundExceedsAmount()
    {
        // Arrange
        var payment = new Payment(1, 100m, "EUR");
        payment.MarkSucceeded("pm_123");

        // Act
        var act = () => payment.Refund(150m);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Refund amount cannot exceed payment amount");
    }

    #endregion

    #region RequireAction Tests

    [Fact]
    public void RequireAction_ShouldSetStatusToRequiresAction()
    {
        // Arrange
        var payment = new Payment(1, 100m, "EUR");
        payment.SetPaymentIntent("pi_123", "secret");

        // Act
        payment.RequireAction();

        // Assert
        payment.Status.Should().Be(PaymentStatus.RequiresAction);
    }

    #endregion

    #region MarkProcessing Tests

    [Fact]
    public void MarkProcessing_ShouldSetStatusToProcessing()
    {
        // Arrange
        var payment = new Payment(1, 100m, "EUR");
        payment.SetPaymentIntent("pi_123", "secret");

        // Act
        payment.MarkProcessing();

        // Assert
        payment.Status.Should().Be(PaymentStatus.Processing);
    }

    #endregion

    #region MarkCanceled Tests

    [Fact]
    public void MarkCanceled_ShouldSetStatusToCanceledAndDeactivate()
    {
        // Arrange
        var payment = new Payment(1, 100m, "EUR");
        payment.SetPaymentIntent("pi_123", "secret");

        // Act
        payment.MarkCanceled();

        // Assert
        payment.Status.Should().Be(PaymentStatus.Canceled);
        payment.IsActive.Should().BeFalse();
    }

    #endregion

    #region UpdateProviderEventId Tests

    [Fact]
    public void UpdateProviderEventId_ShouldSetLastProviderEventId()
    {
        // Arrange
        var payment = new Payment(1, 100m, "EUR");
        var eventId = "evt_123456";

        // Act
        payment.UpdateProviderEventId(eventId);

        // Assert
        payment.LastProviderEventId.Should().Be(eventId);
    }

    [Fact]
    public void UpdateProviderEventId_ShouldAllowMultipleUpdates()
    {
        // Arrange
        var payment = new Payment(1, 100m, "EUR");

        // Act
        payment.UpdateProviderEventId("evt_1");
        payment.UpdateProviderEventId("evt_2");

        // Assert
        payment.LastProviderEventId.Should().Be("evt_2");
    }

    #endregion

    #region State Transition Tests

    [Fact]
    public void Payment_ShouldSupportFullLifecycle_FromCreationToSuccess()
    {
        // Arrange
        var payment = new Payment(1, 100m, "EUR");

        // Act & Assert - Initial state
        payment.Status.Should().Be(PaymentStatus.RequiresPayment);

        // Set payment intent
        payment.SetPaymentIntent("pi_123", "secret");
        payment.Status.Should().Be(PaymentStatus.RequiresPayment);

        // Mark processing
        payment.MarkProcessing();
        payment.Status.Should().Be(PaymentStatus.Processing);

        // Mark succeeded
        payment.MarkSucceeded("pm_123");
        payment.Status.Should().Be(PaymentStatus.Succeeded);
        payment.PaidAt.Should().NotBeNull();
    }

    [Fact]
    public void Payment_ShouldSupportFullLifecycle_FromCreationToRefund()
    {
        // Arrange
        var payment = new Payment(1, 100m, "EUR");

        // Act
        payment.SetPaymentIntent("pi_123", "secret");
        payment.MarkSucceeded("pm_123");
        payment.Refund(100m);

        // Assert
        payment.Status.Should().Be(PaymentStatus.Refunded);
        payment.AmountRefunded.Should().Be(100m);
        payment.RefundedAt.Should().NotBeNull();
    }

    [Fact]
    public void Payment_ShouldSupportRequiresActionFlow()
    {
        // Arrange
        var payment = new Payment(1, 100m, "EUR");
        payment.SetPaymentIntent("pi_123", "secret");

        // Act
        payment.RequireAction();

        // Assert
        payment.Status.Should().Be(PaymentStatus.RequiresAction);
        payment.IsActive.Should().BeTrue();
    }

    #endregion
}
