namespace Api.Middleware
{
	/// <summary>
	/// Middleware that ensures every request has a correlation ID for distributed tracing.
	/// If the client provides an X-Correlation-ID header, it uses that value.
	/// Otherwise, it generates a new unique correlation ID.
	/// The correlation ID is added to the HttpContext and response headers.
	/// </summary>
	public sealed class CorrelationIdMiddleware
	{
		private const string CorrelationIdHeader = "X-Correlation-ID";
		private readonly RequestDelegate _next;
		private readonly ILogger<CorrelationIdMiddleware> _logger;

		public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
		{
			_next = next;
			_logger = logger;
		}

		public async Task InvokeAsync(HttpContext context)
		{
			// Get or generate correlation ID
			var correlationId = context.Request.Headers[CorrelationIdHeader].FirstOrDefault()
				?? Guid.NewGuid().ToString();

			// Store in HttpContext for access throughout the request pipeline
			context.Items["CorrelationId"] = correlationId;

			// Add to response headers for client-side tracing
			context.Response.OnStarting(() =>
			{
				if (!context.Response.Headers.ContainsKey(CorrelationIdHeader))
				{
					context.Response.Headers.Append(CorrelationIdHeader, correlationId);
				}
				return Task.CompletedTask;
			});

			// Add correlation ID to logging scope
			using (_logger.BeginScope(new Dictionary<string, object>
			{
				["CorrelationId"] = correlationId,
				["RequestPath"] = context.Request.Path
			}))
			{
				_logger.LogInformation("Request started for {Method} {Path} with CorrelationId: {CorrelationId}",
					context.Request.Method,
					context.Request.Path,
					correlationId);

				await _next(context);

				_logger.LogInformation("Request completed for {Method} {Path} with status {StatusCode}",
					context.Request.Method,
					context.Request.Path,
					context.Response.StatusCode);
			}
		}
	}
}
