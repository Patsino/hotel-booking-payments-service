# Hotel Booking - Payments Service

Payment processing microservice for the Hotel Booking System, integrated with Stripe.

## Overview

The Payments Service handles all payment operations through Stripe, including payment intents, confirmations, refunds, and webhook processing. It maintains payment state and coordinates with the Reservations Service.

## Key Responsibilities

- **Payment Intent Creation**: Generate Stripe payment intents for reservations
- **Payment Processing**: Handle payment confirmations via Stripe
- **Webhook Processing**: Receive and validate Stripe webhooks
- **Refund Management**: Process full and partial refunds
- **Payment State**: Track payment status throughout lifecycle
- **Reservation Confirmation**: Notify Reservations Service on successful payment

## Technology Stack

- **.NET 9** with ASP.NET Core
- **Entity Framework Core** with SQL Server
- **Stripe .NET SDK** for payment processing
- **JWT** for authentication

## Domain Model

### Entity

**Payment** (Aggregate Root)
- Id, ReservationId, Amount, Currency
- Provider (Stripe)
- Status (RequiresPayment/RequiresAction/Processing/Succeeded/Failed/Refunded/Canceled)
- PaymentMethodType (Card/BankTransfer/Wallet)
- PaymentIntentId (Stripe reference)
- ProviderPaymentId (Stripe charge ID)
- AmountRefunded, RefundedAt
- PaidAt, CreatedAt
- IsActive, LastProviderEventId
- ErrorCode, ErrorMessage

### Enums

- **PaymentStatus**: RequiresPayment, RequiresAction, Processing, Succeeded, Failed, Refunded, Canceled
- **PaymentProvider**: Stripe
- **PaymentMethodType**: Card, BankTransfer, Wallet

## API Endpoints

### User Endpoints

```
POST   /api/payments/create-intent  - Create payment intent for reservation
POST   /api/payments/confirm        - Confirm payment (client-side)
GET    /api/payments/{id}           - Get payment details
GET    /api/payments/reservation/{reservationId} - Get payment by reservation
```

### Admin Endpoints

```
GET    /api/admin/payments          - Get all payments
POST   /api/payments/refund         - Process refund
```

### Webhook Endpoints

```
POST   /api/webhook/stripe          - Stripe webhook receiver
```

### Internal Endpoints (Service-to-Service)

```
POST   /api/internal/payments/refund - Process refund (requires API key)
GET    /api/internal/payments/{id}   - Get payment details
```

## Payment Flow

### 1. Create Payment Intent

```
POST /api/payments/create-intent
{
  "reservationId": 42,
  "amount": 350.00,
  "currency": "EUR"
}

Response:
{
  "paymentId": 123,
  "clientSecret": "pi_xxx_secret_yyy",
  "status": "RequiresPayment"
}
```

**Backend Process:**
1. Validate reservation exists (call Reservations Service)
2. Create payment record in database
3. Create PaymentIntent in Stripe
4. Return client secret for frontend

### 2. Client Confirms Payment

Frontend uses Stripe.js with client secret:
```javascript
const stripe = Stripe('pk_test_...');
const result = await stripe.confirmCardPayment(clientSecret, {
  payment_method: {
    card: cardElement,
    billing_details: { name: 'John Doe' }
  }
});
```

### 3. Stripe Webhook Processing

When payment succeeds, Stripe sends webhook:
```
POST /api/webhook/stripe
Stripe-Signature: t=xxx,v1=yyy

Event: payment_intent.succeeded
```

**Backend Process:**
1. Validate webhook signature (security)
2. Update payment status to `Succeeded`
3. Call Reservations Service to confirm booking
4. Return 200 OK to Stripe

### 4. Reservation Confirmed

Reservations Service receives confirmation:
```
PATCH /api/internal/reservations/{id}/confirm
```

Booking status → `Confirmed` 

## Refund Flow

### Admin-Triggered Refund

```
POST /api/payments/refund
{
  "paymentId": 123,
  "amount": 350.00,  // or partial amount
  "reason": "Customer cancellation"
}
```

**Process:**
1. Validate payment is in `Succeeded` status
2. Create refund in Stripe
3. Update payment record (AmountRefunded, Status → Refunded)
4. Stripe sends `refund.succeeded` webhook (confirmation)

### Service-Triggered Refund

Reservations Service can trigger refund:
```
POST /api/internal/payments/refund
X-API-Key: <api-key>
{
  "reservationId": 42,
  "amount": 350.00
}
```

## Webhook Events Handled

### Payment Events
- `payment_intent.succeeded` → Update payment status, confirm reservation
- `payment_intent.payment_failed` → Update payment status, log error
- `payment_intent.canceled` → Mark payment as canceled

### Refund Events
- `charge.refunded` → Update payment with refund amount
- `charge.refund.updated` → Update refund status

## Configuration

### Environment Variables

```bash
# Database
ConnectionStrings:DefaultConnection=<sql-connection-string>

# Stripe Configuration
Stripe:SecretKey=sk_test_xxx
Stripe:PublishableKey=pk_test_xxx
Stripe:WebhookSecret=whsec_xxx

# Service URLs
ServiceUrls:Users=http://localhost:8081
ServiceUrls:Hotels=http://localhost:8082
ServiceUrls:Reservations=http://localhost:8083
ServiceUrls:Payments=http://localhost:8084

# API Keys
ApiKeys:Services:UsersService=<api-key>
ApiKeys:Services:HotelsService=<api-key>
ApiKeys:Services:ReservationsService=<api-key>

# JWT (for token validation)
Jwt:SecretKey=<base64-secret>
Jwt:Issuer=HotelBookingUsers
Jwt:Audience=HotelBookingAPI
```

## Stripe Setup

### Get API Keys

1. Sign up at [stripe.com](https://stripe.com)
2. Navigate to [Dashboard → API Keys](https://dashboard.stripe.com/test/apikeys)
3. Copy **Test** keys:
   - Secret key (`sk_test_...`)
   - Publishable key (`pk_test_...`)

### Configure Webhooks

**For Local Development:**

```bash
# Install Stripe CLI
stripe login

# Forward webhooks to local service
stripe listen --forward-to https://localhost:7133/api/webhooks/stripe

# Copy webhook signing secret (whsec_...)
# Add to appsettings.json or environment variables
```


## Running locally

### Prerequisites
- .NET 9 SDK
- SQL Server LocalDB installed
- Stripe account (test mode)
- Stripe CLI (for webhooks)

### Standalone Run

```bash
# Navigate to Api project
cd Api

# Update configuration in appsettings.json
# Run migrations
dotnet ef database update

# Run the service
dotnet run
```

Service will be available at `http://localhost:7133/swagger/index.html `

### Docker Compose Run

See main repository's `README-DOCKER.md` for full orchestration setup.

## Database Schema

The service uses the `payments` schema in the shared SQL Server database:

- `payments.Payments` - All payment transactions

## Security

### Webhook Validation

All webhooks are validated using Stripe signature:
```csharp
var stripeEvent = EventUtility.ConstructEvent(
    requestBody, 
    signature, 
    webhookSecret
);
```

Prevents unauthorized webhook calls.

### Idempotency

- Webhook processing is idempotent (uses `LastProviderEventId`)
- Same event processed multiple times = no duplicate actions
- Prevents double-confirmation of reservations

## Business Rules

### Payment Creation
- Reservation must exist and be in `Pending` status
- Amount must be > 0
- Currency must be valid ISO code (EUR, USD, etc.)

### Payment Confirmation
- Only `RequiresPayment` or `RequiresAction` payments can be confirmed
- Stripe PaymentIntent must be in `succeeded` status

### Refunds
- Only `Succeeded` payments can be refunded
- Refund amount cannot exceed payment amount
- Partial refunds are supported

## Integration with Other Services

### Calls TO Other Services
- **Reservations Service**: 
  - Validate reservation exists
  - Confirm reservation after successful payment

### Receives Calls FROM Other Services
- **Reservations Service**: 
  - Request refund for canceled bookings
- **Stripe**: 
  - Webhook notifications

All internal calls require `X-API-Key` header.

## Data Consistency

### Eventual Consistency

Payment confirmation flow uses eventual consistency:
1. Stripe processes payment (1-3 seconds)
2. Webhook arrives (1-5 seconds delay possible)
3. Payment Service confirms reservation

Total delay: 2-8 seconds (acceptable for booking systems)

### Failure Handling

If Reservations Service is unavailable during confirmation:
- Payment remains `Succeeded`
- Webhook can be replayed by Stripe (automatic retries)
- Admin can manually confirm reservation

## Swagger Documentation

Interactive API documentation available at: `http://localhost:7133/swagger/index.html `


## Domain Events

- `PaymentIntentCreated` - Payment intent created in Stripe
- `PaymentSucceeded` - Payment completed successfully
- `PaymentFailed` - Payment failed
- `RefundProcessed` - Refund completed


## Port

- **Development**: 7133
- **Azure**: https://hotel-booking-payments-api-gch8e7fyeqenfje8.northeurope-01.azurewebsites.net
