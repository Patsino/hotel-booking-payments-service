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
