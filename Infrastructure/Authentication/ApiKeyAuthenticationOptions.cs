using Microsoft.AspNetCore.Authentication;
using System.Diagnostics.CodeAnalysis;

namespace Infrastructure.Authentication
{
	[ExcludeFromCodeCoverage]
	public sealed class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions
	{
		public const string DefaultSchemeName = "ApiKey";
		public string HeaderName { get; set; } = "X-API-Key";
	}
}
