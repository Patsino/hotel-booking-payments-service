namespace HotelBooking.Payments.Domain.Payments;

public enum PaymentStatus
{
    RequiresPayment = 0,
    RequiresAction = 1,
    Processing = 2,
    Succeeded = 3,
    Failed = 4,
    Refunded = 5,
    Canceled = 6
}
