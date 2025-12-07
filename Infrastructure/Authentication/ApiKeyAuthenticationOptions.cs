using Microsoft.AspNetCore.Authentication;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
