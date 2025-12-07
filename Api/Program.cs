using Api.Middleware;
using HotelBooking.Payments.Infrastructure;
using HotelBooking.Payments.Infrastructure.Persistence;
using Infrastructure.Authentication;
using Infrastructure.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;

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
		Title = "Hotel Booking - Payments API",
		Version = "v1"
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
			Array.Empty<string>()
		}
	});
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
            logger.LogInformation("Starting database migration...");
            var dbContext = services.GetRequiredService<PaymentsDbContext>();
            await dbContext.Database.MigrateAsync();
            logger.LogInformation("Database migration completed");

            logger.LogInformation("Starting database seeding...");
            var seeder = services.GetRequiredService<PaymentsDataSeeder>();
            await seeder.SeedAsync();
            logger.LogInformation("Database seeding completed");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred during migration or seeding");
            throw;
        }
    }
}

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
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