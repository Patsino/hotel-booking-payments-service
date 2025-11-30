using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Authentication
{
	public sealed class CurrentUserService : ICurrentUserService
	{
		private readonly IHttpContextAccessor _httpContextAccessor;

		public CurrentUserService(IHttpContextAccessor httpContextAccessor)
		{
			_httpContextAccessor = httpContextAccessor;
		}

		private ClaimsPrincipal? User => _httpContextAccessor.HttpContext?.User;

		public int? UserId
		{
			get
			{
				var userIdClaim = User?.FindFirstValue(ClaimTypes.NameIdentifier);
				return int.TryParse(userIdClaim, out var userId) ? userId : null;
			}
		}

		public string? Email => User?.FindFirstValue(ClaimTypes.Email);

		public string? Role => User?.FindFirstValue(ClaimTypes.Role);

		public bool IsAuthenticated => User?.Identity?.IsAuthenticated ?? false;

		public bool IsAdmin => Role == "Admin";

		public bool IsHotelOwner => Role == "HotelOwner";

		public bool IsUser => Role == "User";
	}
}
