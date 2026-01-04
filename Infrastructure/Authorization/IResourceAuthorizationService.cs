namespace Infrastructure.Authorization
{
	public interface IResourceAuthorizationService
	{
		bool CanAccessResource(int resourceOwnerId);
		bool CanModifyResource(int resourceOwnerId);
		void EnsureCanAccessResource(int resourceOwnerId);
		void EnsureCanModifyResource(int resourceOwnerId);
	}
}
