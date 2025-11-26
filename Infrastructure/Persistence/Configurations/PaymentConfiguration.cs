using HotelBooking.Payments.Domain.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HotelBooking.Payments.Infrastructure.Persistence.Configurations;

public sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable(
            name: "Payments",
            schema: "payments",
            buildAction: tableBuilder =>
            {
                tableBuilder.HasCheckConstraint(
                    name: "CK_Payments_Amount_NonNegative",
                    sql: "[Amount] >= 0");
                tableBuilder.HasCheckConstraint(
                    name: "CK_Payments_AmountRefunded_NonNegative",
                    sql: "[AmountRefunded] >= 0");
                tableBuilder.HasCheckConstraint(
                    name: "CK_Payments_Refund_Logic",
                    sql: "[Status] <> 'Refunded' OR [AmountRefunded] > 0");
            });

        builder.HasKey(payment => payment.Id);

        builder.Property(payment => payment.Id)
            .HasColumnName("Id")
            .ValueGeneratedOnAdd();

        builder.Property(payment => payment.ReservationId)
            .HasColumnName("ReservationId")
            .IsRequired();

        builder.Property(payment => payment.Amount)
            .HasColumnName("Amount")
            .HasColumnType("decimal(10,2)")
            .IsRequired();

        builder.Property(payment => payment.Currency)
            .HasColumnName("Currency")
            .IsRequired()
            .HasMaxLength(10)
            .HasDefaultValue("EUR");

        builder.Property(payment => payment.Provider)
            .HasColumnName("Provider")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired()
            .HasDefaultValue(PaymentProvider.Stripe);

        builder.Property(payment => payment.Status)
            .HasColumnName("Status")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired()
            .HasDefaultValue(PaymentStatus.RequiresPayment);

        builder.Property(payment => payment.PaymentMethodType)
            .HasColumnName("PaymentMethodType")
            .HasConversion<string>()
            .HasMaxLength(30);

        builder.Property(payment => payment.PaymentIntentId)
            .HasColumnName("PaymentIntentId")
            .HasMaxLength(100);

        builder.Property(payment => payment.ProviderPaymentId)
            .HasColumnName("ProviderPaymentId")
            .HasMaxLength(100);

        builder.Property(payment => payment.AmountRefunded)
            .HasColumnName("AmountRefunded")
            .HasColumnType("decimal(10,2)")
            .IsRequired()
            .HasDefaultValue(0.00m);

        builder.Property(payment => payment.RefundedAt)
            .HasColumnName("RefundedAt");

        builder.Property(payment => payment.PaidAt)
            .HasColumnName("PaidAt");

        builder.Property(payment => payment.CreatedAt)
            .HasColumnName("CreatedAt")
            .IsRequired()
            .HasColumnType("datetimeoffset(7)")
            .HasDefaultValueSql("SYSUTCDATETIME()");

        builder.Property(payment => payment.IsActive)
            .HasColumnName("IsActive")
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(payment => payment.LastProviderEventId)
            .HasColumnName("LastProviderEventId")
            .HasMaxLength(100);

        builder.Property(payment => payment.ErrorCode)
            .HasColumnName("ErrorCode")
            .HasMaxLength(50);

        builder.Property(payment => payment.ErrorMessage)
            .HasColumnName("ErrorMessage")
            .HasMaxLength(300);

        builder.HasIndex(payment => payment.ReservationId)
            .HasDatabaseName("IX_Payments_ReservationId");

        builder.HasIndex(payment => payment.PaymentIntentId)
            .IsUnique()
            .HasDatabaseName("IX_Payments_PaymentIntentId");

        builder.HasIndex(payment => payment.ProviderPaymentId)
            .HasDatabaseName("IX_Payments_ProviderPaymentId");

        builder.HasIndex(payment => payment.Status)
            .HasDatabaseName("IX_Payments_Status");

        builder.HasIndex(payment => payment.IsActive)
            .HasDatabaseName("IX_Payments_IsActive");

        builder.HasIndex(payment => payment.CreatedAt)
            .HasDatabaseName("IX_Payments_CreatedAt");
    }
}
