using Api.Middleware;
using Api.Filters;
using HotelBooking.Payments.Infrastructure;
using HotelBooking.Payments.Infrastructure.Persistence;
using Infrastructure.Authentication;
using Infrastructure.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using Polly;

// Load .env file for local development (before creating builder)
// VS runs from Api/, but .env is in solution root, so go up one level
var envPath = Path.Combine(Directory.GetCurrentDirectory(), "..", ".env");
if (File.Exists(envPath))
{
	DotNetEnv.Env.Load(envPath);
}

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration, builder.Environment.EnvironmentName);

// ADD AUTHENTICATION
builder.Services.AddJwtAuthentication(builder.Configuration);

builder.Services.AddControllers();

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
	c.SwaggerDoc("v1", new OpenApiInfo
	{
		Title = "Hotel Booking - Payments Service API",
		Version = "v1.0.0",
		Description = @"Payments microservice for processing payments and refunds via Stripe.

**Features:**
- Create Stripe payment intents
- Confirm payments
- Process refunds (full/partial)
- Stripe webhook integration for real-time updates
- View payment history by reservation

**Authentication:** JWT Bearer token required (except webhooks).

**Roles:** User, HotelOwner, Admin

**Payment Provider:** Stripe",
		Contact = new OpenApiContact
		{
			Name = "Hotel Booking System",
			Email = "support@hotelbooking.com"
		}
	});

	// ADD SWAGGER AUTH
	c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
	{
		Description = "JWT Authorization header using the Bearer scheme. Enter 'Bearer' [space] and then your token",
		Name = "Authorization",
		In = ParameterLocation.Header,
		Type = SecuritySchemeType.Http,
		Scheme = "Bearer"
	});

	c.AddSecurityRequirement(new OpenApiSecurityRequirement
	{
		{
			new OpenApiSecurityScheme
			{
				Reference = new OpenApiReference
				{
					Type = ReferenceType.SecurityScheme,
					Id = "Bearer"
				}
			},
			new List<string>()
		}
	});

	var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
	var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
	if (File.Exists(xmlPath))
	{
		c.IncludeXmlComments(xmlPath);
	}

	c.EnableAnnotations();
	c.SchemaFilter<ExampleSchemaFilter>();
});

builder.Services.AddHealthChecks()
	.AddDbContextCheck<PaymentsDbContext>();

builder.Services.AddCors(options =>
{
	options.AddDefaultPolicy(policy =>
	{
		policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
	});
});

var app = builder.Build();

// Skip migration and seeding in Testing environment
if (!app.Environment.IsEnvironment("Testing"))
{
    using (var scope = app.Services.CreateScope())
    {
        var services = scope.ServiceProvider;
        var logger = services.GetRequiredService<ILogger<Program>>();

        try
        {
            var dbContext = services.GetRequiredService<PaymentsDbContext>();

            // For Production (Azure Free tier with cold start), use retry logic with longer timeout
            if (app.Environment.IsProduction())
            {
                logger.LogInformation("Production environment detected. Using retry policy for cold database start...");

                // Configure retry policy with exponential backoff for cold Azure DB
                var retryPolicy = Policy
                    .Handle<Exception>()
                    .WaitAndRetryAsync(
                        retryCount: 5,
                        sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)), // 2, 4, 8, 16, 32 sec
                        onRetry: (exception, timeSpan, retryCount, context) =>
                        {
                            logger.LogWarning(
                                "Database connection attempt {RetryCount} failed. Waiting {WaitSeconds}s before next retry. Error: {Error}",
                                retryCount,
                                timeSpan.TotalSeconds,
                                exception.Message);
                        });

                // Set longer command timeout for cold database (default is 30 sec)
                dbContext.Database.SetCommandTimeout(TimeSpan.FromSeconds(90));

                await retryPolicy.ExecuteAsync(async () =>
                {
                    logger.LogInformation("Starting database migration...");
                    await dbContext.Database.MigrateAsync();
                    logger.LogInformation("Database migration completed");
                });

                await retryPolicy.ExecuteAsync(async () =>
                {
                    logger.LogInformation("Starting database seeding...");
                    var seeder = services.GetRequiredService<PaymentsDataSeeder>();
                    await seeder.SeedAsync();
                    logger.LogInformation("Database seeding completed");
                });
            }
            else
            {
                // Development/Staging: use default behavior (fast local DB)
                logger.LogInformation("Starting database migration...");
                await dbContext.Database.MigrateAsync();
                logger.LogInformation("Database migration completed");

                logger.LogInformation("Starting database seeding...");
                var seeder = services.GetRequiredService<PaymentsDataSeeder>();
                await seeder.SeedAsync();
                logger.LogInformation("Database seeding completed");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred during migration or seeding");
            throw;
        }
    }
}

app.UseExceptionHandler();

// Register correlation ID middleware EARLY in the pipeline
app.UseMiddleware<Api.Middleware.CorrelationIdMiddleware>();

if (app.Environment.IsDevelopment() || app.Environment.IsProduction())
{
    app.UseSwagger();
	app.UseSwaggerUI();
}

app.UseCors();

// ADD THESE BEFORE MapControllers
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();