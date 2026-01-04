using Application.Handlers;
using Application.Services;
using HotelBooking.Payments.Infrastructure.Persistence;
using Infrastructure.Authentication;
using Infrastructure.Authorization;
using Infrastructure.Http;
using Infrastructure.Repsitories;
using Infrastructure.Seeding;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.Extensions.Http;
using System.Diagnostics.CodeAnalysis;

namespace HotelBooking.Payments.Infrastructure;

[ExcludeFromCodeCoverage]
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        string? environmentName = null)
    {
        // Use InMemory database for Testing environment
        if (environmentName == "Testing")
        {
            services.AddDbContext<PaymentsDbContext>(options =>
            {
                options.UseInMemoryDatabase("TestDatabase");
            });
        }
        else
        {
            var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__HotelBookingDatabase")
				?? configuration.GetConnectionString("HotelBookingDatabase");

			if (string.IsNullOrWhiteSpace(connectionString))
			{
				throw new InvalidOperationException("ConnectionStrings:HotelBookingDatabase is not configured.");
			}

			services.AddDbContext<PaymentsDbContext>(options =>
			{
				options.UseSqlServer(connectionString, sqlOptions =>
				{
					sqlOptions.MigrationsHistoryTable("__EFMigrationsHistory", "payments");
				});
			});
        }

		services.AddScoped<IPaymentsRepository, PaymentsRepository>();
		services.AddScoped<CreatePaymentHandler>();

		services.AddSingleton<IStripePaymentService, StripePaymentService>();
		services.AddScoped<ICreatePaymentIntentHandler, CreatePaymentIntentHandler>();
		services.AddScoped<IConfirmPaymentHandler, ConfirmPaymentHandler>();
		services.AddScoped<IRefundPaymentHandler, RefundPaymentHandler>();

		services.AddHttpContextAccessor();
		services.AddScoped<ICurrentUserService, CurrentUserService>();
		services.AddScoped<IResourceAuthorizationService, ResourceAuthorizationService>();

		services.AddTransient<AuthenticatedHttpClientHandler>();


	var retryPolicy = HttpPolicyExtensions
		.HandleTransientHttpError()
		.WaitAndRetryAsync(
			retryCount: 3,
			sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(0.5 * Math.Pow(2, retryAttempt)),
			onRetry: (outcome, timespan, retryAttempt, context) =>
			{
				Console.WriteLine($"Retry {retryAttempt} after {timespan.TotalSeconds}s due to: {outcome.Exception?.Message ?? outcome.Result?.StatusCode.ToString()}");
			});

	var timeoutPolicy = Policy.TimeoutAsync<HttpResponseMessage>(TimeSpan.FromSeconds(5));		services.AddHttpClient("ReservationsService", client =>
		{
			var baseUrl = Environment.GetEnvironmentVariable("ServiceUrls__Reservations")
				?? configuration["ServiceUrls:Reservations"] 
				?? "http://localhost:5003";
			client.BaseAddress = new Uri(baseUrl);
			client.Timeout = TimeSpan.FromSeconds(15);
		})
		.AddHttpMessageHandler<AuthenticatedHttpClientHandler>()
		.AddPolicyHandler(retryPolicy)
		.AddPolicyHandler(timeoutPolicy);

		services.AddHttpClient("HotelsService", client =>
		{
			var baseUrl = Environment.GetEnvironmentVariable("ServiceUrls__Hotels")
				?? configuration["ServiceUrls:Hotels"] 
				?? "http://localhost:5002";
			client.BaseAddress = new Uri(baseUrl);
			client.Timeout = TimeSpan.FromSeconds(15);
		})
		.AddHttpMessageHandler<AuthenticatedHttpClientHandler>()
		.AddPolicyHandler(retryPolicy)
		.AddPolicyHandler(timeoutPolicy);

		services.AddHttpClient("PaymentsService", client =>
		{
			var baseUrl = Environment.GetEnvironmentVariable("ServiceUrls__Payments")
				?? configuration["ServiceUrls:Payments"] 
				?? "http://localhost:5004";
			client.BaseAddress = new Uri(baseUrl);
			client.Timeout = TimeSpan.FromSeconds(15);
		})
		.AddHttpMessageHandler<AuthenticatedHttpClientHandler>()
		.AddPolicyHandler(retryPolicy)
		.AddPolicyHandler(timeoutPolicy);

		services.AddHttpClient("UsersService", client =>
		{
			var baseUrl = Environment.GetEnvironmentVariable("ServiceUrls__Users")
				?? configuration["ServiceUrls:Users"] 
				?? "http://localhost:5001";
			client.BaseAddress = new Uri(baseUrl);
			client.Timeout = TimeSpan.FromSeconds(15);
		})
		.AddHttpMessageHandler<AuthenticatedHttpClientHandler>()
		.AddPolicyHandler(retryPolicy)
		.AddPolicyHandler(timeoutPolicy);

		services.AddScoped<PaymentsDataSeeder>();

		return services;
    }
}
