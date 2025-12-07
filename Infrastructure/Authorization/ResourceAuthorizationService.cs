using Infrastructure.Authentication;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Authorization
{
	[ExcludeFromCodeCoverage]
	public sealed class ResourceAuthorizationService : IResourceAuthorizationService
	{
		private readonly ICurrentUserService _currentUser;

		public ResourceAuthorizationService(ICurrentUserService currentUser)
		{
			_currentUser = currentUser;
		}

		public bool CanAccessResource(int resourceOwnerId)
		{
			if (!_currentUser.IsAuthenticated)
				return false;

			// Admin can access everything
			if (_currentUser.IsAdmin)
				return true;

			// Owner can access their own resources
			return _currentUser.UserId == resourceOwnerId;
		}

		public bool CanModifyResource(int resourceOwnerId)
		{
			if (!_currentUser.IsAuthenticated)
				return false;

			// Admin can modify everything
			if (_currentUser.IsAdmin)
				return true;

			// Owner can modify their own resources
			return _currentUser.UserId == resourceOwnerId;
		}

		public void EnsureCanAccessResource(int resourceOwnerId)
		{
			if (!CanAccessResource(resourceOwnerId))
			{
				throw new UnauthorizedAccessException(
					"You do not have permission to access this resource");
			}
		}

		public void EnsureCanModifyResource(int resourceOwnerId)
		{
			if (!CanModifyResource(resourceOwnerId))
			{
				throw new UnauthorizedAccessException(
					"You do not have permission to modify this resource");
			}
		}
	}

}
