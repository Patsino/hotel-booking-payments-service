using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.Dtos
{
	public sealed record ReservationDto(
		int Id,
		int UserId,
		int RoomId,
		DateTime StartDate,
		DateTime EndDate,
		string Status);
}
