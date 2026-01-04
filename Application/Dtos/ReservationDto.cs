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
