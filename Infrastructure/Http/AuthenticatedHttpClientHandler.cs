using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Http
{
	[ExcludeFromCodeCoverage]
	public sealed class AuthenticatedHttpClientHandler : DelegatingHandler
	{
		private readonly IHttpContextAccessor _httpContextAccessor;
		private readonly IConfiguration _configuration;
		private readonly ILogger<AuthenticatedHttpClientHandler> _logger;

		public AuthenticatedHttpClientHandler(
			IHttpContextAccessor httpContextAccessor,
			IConfiguration configuration,
			ILogger<AuthenticatedHttpClientHandler> logger)
		{
			_httpContextAccessor = httpContextAccessor;
			_configuration = configuration;
			_logger = logger;
		}

		protected override async Task<HttpResponseMessage> SendAsync(
			HttpRequestMessage request,
			CancellationToken cancellationToken)
		{
			// ALWAYS add service API key for service-to-service auth
			var apiKey = _configuration["ApiKeys:Current"];
			if (!string.IsNullOrEmpty(apiKey))
			{
				request.Headers.Add("X-API-Key", apiKey);
				_logger.LogDebug("Added service API key to request: {Uri}", request.RequestUri);
			}

			// ALSO forward user JWT if available (for user context)
			var httpContext = _httpContextAccessor.HttpContext;
			if (httpContext != null)
			{
				var authHeader = httpContext.Request.Headers["Authorization"].ToString();
				if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer "))
				{
					request.Headers.Remove("Authorization");
					request.Headers.TryAddWithoutValidation("Authorization", authHeader);
					_logger.LogDebug("Forwarded user JWT to request: {Uri}", request.RequestUri);
				}
			}

			return await base.SendAsync(request, cancellationToken);
		}
	}
}
