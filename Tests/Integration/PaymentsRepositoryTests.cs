using HotelBooking.Payments.Domain.Payments;
using HotelBooking.Payments.Infrastructure.Persistence;
using Infrastructure.Repsitories;
using Microsoft.EntityFrameworkCore;

namespace Tests.Integration;

public sealed class PaymentsRepositoryTests : IDisposable
{
    private readonly PaymentsDbContext _context;
    private readonly PaymentsRepository _repository;

    public PaymentsRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<PaymentsDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new PaymentsDbContext(options);
        _repository = new PaymentsRepository(_context);
    }

    public void Dispose()
    {
        _context.Dispose();
    }

    #region AddAsync Tests

    [Fact]
    public async Task AddAsync_ShouldAddPaymentToDatabase()
    {
        // Arrange
        var payment = new Payment(1, 100m, "EUR");

        // Act
        await _repository.AddAsync(payment);
        await _repository.SaveChangesAsync();

        // Assert
        var savedPayment = await _context.Payments.FirstOrDefaultAsync();
        savedPayment.Should().NotBeNull();
        savedPayment!.ReservationId.Should().Be(1);
        savedPayment.Amount.Should().Be(100m);
        savedPayment.Currency.Should().Be("EUR");
    }

    [Fact]
    public async Task AddAsync_ShouldGenerateId()
    {
        // Arrange
        var payment = new Payment(1, 100m, "EUR");

        // Act
        await _repository.AddAsync(payment);
        await _repository.SaveChangesAsync();

        // Assert
        payment.Id.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task AddAsync_ShouldAddMultiplePayments()
    {
        // Arrange
        var payment1 = new Payment(1, 100m, "EUR");
        var payment2 = new Payment(2, 200m, "USD");

        // Act
        await _repository.AddAsync(payment1);
        await _repository.AddAsync(payment2);
        await _repository.SaveChangesAsync();

        // Assert
        var count = await _context.Payments.CountAsync();
        count.Should().Be(2);
    }

    #endregion

    #region GetByIdAsync Tests

    [Fact]
    public async Task GetByIdAsync_ShouldReturnPayment_WhenExists()
    {
        // Arrange
        var payment = new Payment(1, 100m, "EUR");
        await _context.Payments.AddAsync(payment);
        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.GetByIdAsync(payment.Id);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(payment.Id);
        result.Amount.Should().Be(100m);
    }

    [Fact]
    public async Task GetByIdAsync_ShouldReturnNull_WhenNotExists()
    {
        // Act
        var result = await _repository.GetByIdAsync(999);

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region GetByReservationIdAsync Tests

    [Fact]
    public async Task GetByReservationIdAsync_ShouldReturnPayments_ForReservation()
    {
        // Arrange
        var payment1 = new Payment(1, 100m, "EUR");
        var payment2 = new Payment(1, 150m, "EUR");
        var payment3 = new Payment(2, 200m, "EUR"); // Different reservation

        await _context.Payments.AddRangeAsync(payment1, payment2, payment3);
        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.GetByReservationIdAsync(1);

        // Assert
        result.Should().HaveCount(2);
        result.All(p => p.ReservationId == 1).Should().BeTrue();
    }

    [Fact]
    public async Task GetByReservationIdAsync_ShouldReturnOrderedByCreatedAtDescending()
    {
        // Arrange
        var payment1 = new Payment(1, 100m, "EUR");
        await _context.Payments.AddAsync(payment1);
        await _context.SaveChangesAsync();

        await Task.Delay(10); // Ensure different timestamps

        var payment2 = new Payment(1, 150m, "EUR");
        await _context.Payments.AddAsync(payment2);
        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.GetByReservationIdAsync(1);

        // Assert
        result.Should().HaveCount(2);
        result[0].CreatedAt.Should().BeOnOrAfter(result[1].CreatedAt);
    }

    [Fact]
    public async Task GetByReservationIdAsync_ShouldReturnEmptyList_WhenNoPaymentsExist()
    {
        // Act
        var result = await _repository.GetByReservationIdAsync(999);

        // Assert
        result.Should().BeEmpty();
    }

    #endregion

    #region GetByPaymentIntentIdAsync Tests

    [Fact]
    public async Task GetByPaymentIntentIdAsync_ShouldReturnPayments_WithMatchingPaymentIntentId()
    {
        // Arrange
        var payment = new Payment(1, 100m, "EUR");
        payment.SetPaymentIntent("pi_123456", "secret");
        await _context.Payments.AddAsync(payment);
        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.GetByPaymentIntentIdAsync("pi_123456");

        // Assert
        result.Should().HaveCount(1);
        result[0].PaymentIntentId.Should().Be("pi_123456");
    }

    [Fact]
    public async Task GetByPaymentIntentIdAsync_ShouldReturnEmptyList_WhenPaymentIntentIdNotFound()
    {
        // Arrange
        var payment = new Payment(1, 100m, "EUR");
        payment.SetPaymentIntent("pi_123456", "secret");
        await _context.Payments.AddAsync(payment);
        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.GetByPaymentIntentIdAsync("pi_nonexistent");

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetByPaymentIntentIdAsync_ShouldNotReturnPayments_WithNullPaymentIntentId()
    {
        // Arrange
        var paymentWithIntent = new Payment(1, 100m, "EUR");
        paymentWithIntent.SetPaymentIntent("pi_123456", "secret");

        var paymentWithoutIntent = new Payment(2, 200m, "EUR");

        await _context.Payments.AddRangeAsync(paymentWithIntent, paymentWithoutIntent);
        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.GetByPaymentIntentIdAsync("pi_123456");

        // Assert
        result.Should().HaveCount(1);
        result[0].PaymentIntentId.Should().Be("pi_123456");
    }

    #endregion

    #region SaveChangesAsync Tests

    [Fact]
    public async Task SaveChangesAsync_ShouldPersistChanges()
    {
        // Arrange
        var payment = new Payment(1, 100m, "EUR");
        await _context.Payments.AddAsync(payment);
        await _context.SaveChangesAsync();

        // Act
        payment.MarkSucceeded("pm_123");
        await _repository.SaveChangesAsync();

        // Assert - Verify the changes were persisted
        var savedPayment = await _context.Payments.FindAsync(payment.Id);
        savedPayment!.Status.Should().Be(PaymentStatus.Succeeded);
    }

    #endregion

    #region State Transition Integration Tests

    [Fact]
    public async Task Payment_ShouldPersistStateTransitions_SuccessFlow()
    {
        // Arrange
        var payment = new Payment(1, 100m, "EUR");
        await _repository.AddAsync(payment);
        await _repository.SaveChangesAsync();

        // Act - Set payment intent
        payment.SetPaymentIntent("pi_123", "secret");
        await _repository.SaveChangesAsync();

        // Act - Mark processing
        payment.MarkProcessing();
        await _repository.SaveChangesAsync();

        // Act - Mark succeeded
        payment.MarkSucceeded("pm_123");
        await _repository.SaveChangesAsync();

        // Assert
        var savedPayment = await _repository.GetByIdAsync(payment.Id);
        savedPayment!.Status.Should().Be(PaymentStatus.Succeeded);
        savedPayment.PaymentIntentId.Should().Be("pi_123");
        savedPayment.ProviderPaymentId.Should().Be("pm_123");
        savedPayment.PaidAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Payment_ShouldPersistStateTransitions_RefundFlow()
    {
        // Arrange
        var payment = new Payment(1, 100m, "EUR");
        payment.SetPaymentIntent("pi_123", "secret");
        payment.MarkSucceeded("pm_123");
        await _repository.AddAsync(payment);
        await _repository.SaveChangesAsync();

        // Act
        payment.Refund(100m);
        await _repository.SaveChangesAsync();

        // Assert
        var savedPayment = await _repository.GetByIdAsync(payment.Id);
        savedPayment!.Status.Should().Be(PaymentStatus.Refunded);
        savedPayment.AmountRefunded.Should().Be(100m);
        savedPayment.RefundedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Payment_ShouldPersistErrorDetails()
    {
        // Arrange
        var payment = new Payment(1, 100m, "EUR");
        await _repository.AddAsync(payment);
        await _repository.SaveChangesAsync();

        // Act
        payment.MarkFailed("card_declined", "Your card was declined");
        await _repository.SaveChangesAsync();

        // Assert
        var savedPayment = await _repository.GetByIdAsync(payment.Id);
        savedPayment!.Status.Should().Be(PaymentStatus.Failed);
        savedPayment.ErrorCode.Should().Be("card_declined");
        savedPayment.ErrorMessage.Should().Be("Your card was declined");
        savedPayment.IsActive.Should().BeFalse();
    }

    #endregion

    #region Concurrent Access Tests

    [Fact]
    public async Task MultiplePayments_ShouldBeAddedConcurrently()
    {
        // Arrange
        var tasks = Enumerable.Range(1, 5).Select(async i =>
        {
            var payment = new Payment(i, i * 100m, "EUR");
            await _repository.AddAsync(payment);
        });

        // Act
        await Task.WhenAll(tasks);
        await _repository.SaveChangesAsync();

        // Assert
        var count = await _context.Payments.CountAsync();
        count.Should().Be(5);
    }

    #endregion
}
