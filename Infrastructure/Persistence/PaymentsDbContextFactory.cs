using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using System.Diagnostics.CodeAnalysis;

namespace HotelBooking.Payments.Infrastructure.Persistence;

[ExcludeFromCodeCoverage]
public sealed class PaymentsDbContextFactory : IDesignTimeDbContextFactory<PaymentsDbContext>
{
    public PaymentsDbContext CreateDbContext(string[] args)
    {
		var connectionString =
			Environment.GetEnvironmentVariable("ConnectionStrings__HotelBookingDatabase");

		if (string.IsNullOrWhiteSpace(connectionString))
		{
			connectionString = "Server=(localdb)\\mssqllocaldb;Database=HotelBooking;Trusted_Connection=True;TrustServerCertificate=True;";
		}

		if (string.IsNullOrWhiteSpace(connectionString))
		{
			throw new InvalidOperationException(
				"ConnectionStrings:HotelBookingDatabase is not configured for design-time."
			);
		}

		var optionsBuilder = new DbContextOptionsBuilder<PaymentsDbContext>();

        optionsBuilder.UseSqlServer(connectionString, sqlOptions =>
        {
            sqlOptions.MigrationsHistoryTable("__EFMigrationsHistory", "payments");
        });

        return new PaymentsDbContext(optionsBuilder.Options);
    }
}
