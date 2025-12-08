using Microsoft.AspNetCore.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net;

namespace Api.Middleware
{
	[ExcludeFromCodeCoverage]
	public sealed class GlobalExceptionHandler : IExceptionHandler
	{
		private readonly ILogger<GlobalExceptionHandler> _logger;

		public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
		{
			_logger = logger;
		}

		public async ValueTask<bool> TryHandleAsync(
			HttpContext httpContext,
			Exception exception,
			CancellationToken cancellationToken)
		{
			// Get correlation ID from context
			var correlationId = httpContext.Items["CorrelationId"]?.ToString() ?? "unknown";

			_logger.LogError(exception, 
				"An unhandled exception occurred. CorrelationId: {CorrelationId}, Message: {Message}", 
				correlationId, 
				exception.Message);

			var (statusCode, message) = exception switch
			{
				UnauthorizedAccessException => (HttpStatusCode.Unauthorized, "Unauthorized access"),
				InvalidOperationException => (HttpStatusCode.BadRequest, exception.Message),
				_ => (HttpStatusCode.InternalServerError, "An unexpected error occurred")
			};

			httpContext.Response.StatusCode = (int)statusCode;
			await httpContext.Response.WriteAsJsonAsync(new
			{
				error = message,
				correlationId = correlationId,
				timestamp = DateTime.UtcNow
			}, cancellationToken);

			return true;
		}
	}
}
