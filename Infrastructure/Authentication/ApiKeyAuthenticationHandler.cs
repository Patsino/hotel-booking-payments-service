using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using System.Text.Encodings.Web;

namespace Infrastructure.Authentication
{
	[ExcludeFromCodeCoverage]
	public sealed class ApiKeyAuthenticationHandler : AuthenticationHandler<ApiKeyAuthenticationOptions>
	{
		private readonly IConfiguration _configuration;

		public ApiKeyAuthenticationHandler(
			IOptionsMonitor<ApiKeyAuthenticationOptions> options,
			ILoggerFactory logger,
			UrlEncoder encoder,
			IConfiguration configuration)
			: base(options, logger, encoder)
		{
			_configuration = configuration;
		}

		protected override Task<AuthenticateResult> HandleAuthenticateAsync()
		{
			if (!Request.Headers.TryGetValue(Options.HeaderName, out var apiKeyHeaderValues))
			{
				return Task.FromResult(AuthenticateResult.NoResult());
			}

			var providedApiKey = apiKeyHeaderValues.FirstOrDefault();
			if (string.IsNullOrWhiteSpace(providedApiKey))
			{
				return Task.FromResult(AuthenticateResult.NoResult());
			}

			var validApiKeys = new Dictionary<string, string>();
			
			var envUsersKey = Environment.GetEnvironmentVariable("ApiKeys__Services__UsersService");
			var envHotelsKey = Environment.GetEnvironmentVariable("ApiKeys__Services__HotelsService");
			var envReservationsKey = Environment.GetEnvironmentVariable("ApiKeys__Services__ReservationsService");
			var envPaymentsKey = Environment.GetEnvironmentVariable("ApiKeys__Services__PaymentsService");
			
			if (!string.IsNullOrEmpty(envUsersKey) || !string.IsNullOrEmpty(envHotelsKey) || 
			    !string.IsNullOrEmpty(envReservationsKey) || !string.IsNullOrEmpty(envPaymentsKey))
			{
				if (!string.IsNullOrEmpty(envUsersKey))
					validApiKeys["UsersService"] = envUsersKey;
				if (!string.IsNullOrEmpty(envHotelsKey))
					validApiKeys["HotelsService"] = envHotelsKey;
				if (!string.IsNullOrEmpty(envReservationsKey))
					validApiKeys["ReservationsService"] = envReservationsKey;
				if (!string.IsNullOrEmpty(envPaymentsKey))
					validApiKeys["PaymentsService"] = envPaymentsKey;
			}
			else
			{
				validApiKeys = _configuration.GetSection("ApiKeys:Services").Get<Dictionary<string, string>>() 
					?? new Dictionary<string, string>();
			}
			
			if (validApiKeys.Count == 0 || !validApiKeys.Values.Contains(providedApiKey))
			{
				Logger.LogWarning("Invalid API key provided");
				return Task.FromResult(AuthenticateResult.Fail("Invalid API Key"));
			}

			var serviceName = validApiKeys.FirstOrDefault(x => x.Value == providedApiKey).Key;

			var claims = new[]
			{
				new Claim(ClaimTypes.Name, serviceName),
				new Claim(ClaimTypes.Role, "Service"),
				new Claim("ServiceName", serviceName)
			};

			var identity = new ClaimsIdentity(claims, Scheme.Name);
			var principal = new ClaimsPrincipal(identity);
			var ticket = new AuthenticationTicket(principal, Scheme.Name);

			return Task.FromResult(AuthenticateResult.Success(ticket));
		}
	}
}
