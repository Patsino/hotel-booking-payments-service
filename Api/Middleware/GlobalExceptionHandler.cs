using Microsoft.AspNetCore.Diagnostics;
using System.Net;

namespace Api.Middleware
{
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
			_logger.LogError(exception, "An unhandled exception occurred: {Message}", exception.Message);

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
				timestamp = DateTime.UtcNow
			}, cancellationToken);

			return true;
		}
	}
}
