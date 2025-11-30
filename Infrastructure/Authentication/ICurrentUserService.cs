using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Authentication
{
	public interface ICurrentUserService
	{
		int? UserId { get; }
		string? Email { get; }
		string? Role { get; }
		bool IsAuthenticated { get; }
		bool IsAdmin { get; }
		bool IsHotelOwner { get; }
		bool IsUser { get; }
	}
}
