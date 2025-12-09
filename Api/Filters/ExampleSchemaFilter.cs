using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;
using Application.Commands;
using Application.Dtos;

namespace Api.Filters;

public class ExampleSchemaFilter : ISchemaFilter
{
	public void Apply(OpenApiSchema schema, SchemaFilterContext context)
	{
		if (context.Type == typeof(CreatePaymentIntentCommand))
		{
			schema.Example = new OpenApiObject
			{
				["reservationId"] = new OpenApiInteger(123),
				["amount"] = new OpenApiDouble(450.00),
				["currency"] = new OpenApiString("EUR")
			};
		}
		else if (context.Type == typeof(PaymentIntentResponse))
		{
			schema.Example = new OpenApiObject
			{
				["paymentId"] = new OpenApiInteger(789),
				["paymentIntentId"] = new OpenApiString("pi_3MtwBwLkdIwHu7ix28a3tqPa"),
				["clientSecret"] = new OpenApiString("pi_3MtwBwLkdIwHu7ix28a3tqPa_secret_YGxhckR0OGRZdE0zcqSbW"),
				["amount"] = new OpenApiDouble(450.00),
				["currency"] = new OpenApiString("EUR")
			};
		}
		else if (context.Type == typeof(ConfirmPaymentCommand))
		{
			schema.Example = new OpenApiObject
			{
				["paymentIntentId"] = new OpenApiString("pi_3MtwBwLkdIwHu7ix28a3tqPa"),
				["paymentMethodId"] = new OpenApiString("pm_card_visa")
			};
		}
		else if (context.Type == typeof(RefundPaymentCommand))
		{
			schema.Example = new OpenApiObject
			{
				["paymentId"] = new OpenApiInteger(789),
				["reason"] = new OpenApiString("Customer requested cancellation")
			};
		}
		else if (context.Type == typeof(CreatePaymentCommand))
		{
			schema.Example = new OpenApiObject
			{
				["reservationId"] = new OpenApiInteger(123),
				["amount"] = new OpenApiDouble(450.00),
				["currency"] = new OpenApiString("EUR"),
				["paymentMethod"] = new OpenApiString("Stripe")
			};
		}
	}
}
