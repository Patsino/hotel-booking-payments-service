using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Infrastructure.Authentication
{
	[ExcludeFromCodeCoverage]
	public static class JwtAuthenticationExtensions
	{
		public static IServiceCollection AddJwtAuthentication(
			this IServiceCollection services,
			IConfiguration configuration)
		{
			var jwtSettings = configuration.GetSection("Jwt");
			
			var secretKey = Environment.GetEnvironmentVariable("Jwt__SecretKey")
				?? jwtSettings["SecretKey"]
				?? throw new InvalidOperationException("JWT SecretKey not configured");
			
			var issuer = Environment.GetEnvironmentVariable("Jwt__Issuer")
				?? jwtSettings["Issuer"]
				?? "HotelBooking";
			
			var audience = Environment.GetEnvironmentVariable("Jwt__Audience")
				?? jwtSettings["Audience"]
				?? "HotelBookingUsers";

			services.AddAuthentication(options =>
			{
				options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
				options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
			})
			.AddJwtBearer(options =>
			{
				options.TokenValidationParameters = new TokenValidationParameters
				{
					ValidateIssuer = true,
					ValidateAudience = true,
					ValidateLifetime = true,
					ValidateIssuerSigningKey = true,
					ValidIssuer = issuer,
					ValidAudience = audience,
					IssuerSigningKey = new SymmetricSecurityKey(
						Encoding.UTF8.GetBytes(secretKey)),
					ClockSkew = TimeSpan.FromMinutes(5)
				};
			})
			.AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(
				ApiKeyAuthenticationOptions.DefaultSchemeName,
				options => { });

			services.AddAuthorization(options =>
			{
				options.AddPolicy("Authenticated", policy =>
					policy.RequireAuthenticatedUser());

				options.AddPolicy("AdminOnly", policy =>
					policy.RequireRole("Admin"));

				options.AddPolicy("HotelOwnerOrAdmin", policy =>
					policy.RequireRole("HotelOwner", "Admin"));

				options.AddPolicy("RegisteredUser", policy =>
					policy.RequireRole("User", "HotelOwner", "Admin"));

				options.AddPolicy("ServiceToService", policy =>
					policy.RequireAssertion(context =>
					{
						if (context.User.IsInRole("Service"))
							return true;

						if (context.User.IsInRole("User") ||
							context.User.IsInRole("HotelOwner") ||
							context.User.IsInRole("Admin"))
							return true;

						return false;
					}));
			});

			return services;
		}
	}
}
