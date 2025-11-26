using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "payments");

            migrationBuilder.CreateTable(
                name: "Payments",
                schema: "payments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReservationId = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false, defaultValue: "EUR"),
                    Provider = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "Stripe"),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false, defaultValue: "RequiresPayment"),
                    PaymentMethodType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    PaymentIntentId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ProviderPaymentId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    AmountRefunded = table.Column<decimal>(type: "decimal(10,2)", nullable: false, defaultValue: 0.00m),
                    RefundedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PaidAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    LastProviderEventId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ErrorCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Payments", x => x.Id);
                    table.CheckConstraint("CK_Payments_Amount_NonNegative", "[Amount] >= 0");
                    table.CheckConstraint("CK_Payments_AmountRefunded_NonNegative", "[AmountRefunded] >= 0");
                    table.CheckConstraint("CK_Payments_Refund_Logic", "[Status] <> 'Refunded' OR [AmountRefunded] > 0");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Payments_CreatedAt",
                schema: "payments",
                table: "Payments",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_IsActive",
                schema: "payments",
                table: "Payments",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_PaymentIntentId",
                schema: "payments",
                table: "Payments",
                column: "PaymentIntentId",
                unique: true,
                filter: "[PaymentIntentId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_ProviderPaymentId",
                schema: "payments",
                table: "Payments",
                column: "ProviderPaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_ReservationId",
                schema: "payments",
                table: "Payments",
                column: "ReservationId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_Status",
                schema: "payments",
                table: "Payments",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Payments",
                schema: "payments");
        }
    }
}
