using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.IdentityModel.Tokens;
using POS.API.Filters;
using POS.API.Hubs;
using POS.API.Middleware;
using POS.API.Workers;
using POS.Domain.Settings;
using Microsoft.EntityFrameworkCore;
using Stripe;
using POS.Repository;
using POS.Repository.Dependencies;
using POS.Services.Adapter;
using POS.Services.Dependencies;
using Serilog;

QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

var builder = WebApplication.CreateBuilder(args);

// Override connection string from DATABASE_URL environment variable (Render/Production)
var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
if (!string.IsNullOrEmpty(databaseUrl))
{
    builder.Configuration["ConnectionStrings:DefaultConnection"] = databaseUrl;
}

var jwtSecret = Environment.GetEnvironmentVariable("JWT_SECRET");
if (!string.IsNullOrEmpty(jwtSecret))
{
    builder.Configuration["Jwt:Secret"] = jwtSecret;
}

var vapidPublic = Environment.GetEnvironmentVariable("VAPID_PUBLIC");
if (!string.IsNullOrEmpty(vapidPublic))
    builder.Configuration["Vapid:PublicKey"] = vapidPublic;

var vapidPrivate = Environment.GetEnvironmentVariable("VAPID_PRIVATE");
if (!string.IsNullOrEmpty(vapidPrivate))
    builder.Configuration["Vapid:PrivateKey"] = vapidPrivate;

var supabaseUrl = Environment.GetEnvironmentVariable("SUPABASE_URL");
if (!string.IsNullOrEmpty(supabaseUrl))
    builder.Configuration["Supabase:Url"] = supabaseUrl;

var supabaseKey = Environment.GetEnvironmentVariable("SUPABASE_SERVICE_KEY");
if (!string.IsNullOrEmpty(supabaseKey))
    builder.Configuration["Supabase:ServiceKey"] = supabaseKey;

var stripeSecret = Environment.GetEnvironmentVariable("STRIPE_SECRET_KEY");
if (!string.IsNullOrEmpty(stripeSecret))
    builder.Configuration["Stripe:SecretKey"] = stripeSecret;

var stripeWebhookSecret = Environment.GetEnvironmentVariable("STRIPE_WEBHOOK_SECRET");
if (!string.IsNullOrEmpty(stripeWebhookSecret))
    builder.Configuration["Stripe:WebhookSecret"] = stripeWebhookSecret;

var resendApiKey = Environment.GetEnvironmentVariable("RESEND_API_KEY");
if (!string.IsNullOrEmpty(resendApiKey))
    builder.Configuration["Email:ApiKey"] = resendApiKey;

// Configure Serilog
builder.Host.UseSerilog((context, configuration) =>
    configuration.ReadFrom.Configuration(context.Configuration));

// Add services to the container.
builder.Services.AddControllers(options =>
    {
        // BDD-014: close the 10-year device token loophole by gating every MVC
        // action against Device.IsActive when the JWT carries type=device.
        options.Filters.Add<DeviceActiveAuthorizationFilter>();
    })
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.ReferenceHandler =
            System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
        options.JsonSerializerOptions.DefaultIgnoreCondition =
            System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
        options.JsonSerializerOptions.Converters.Add(
            new System.Text.Json.Serialization.JsonStringEnumConverter());
    });

builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen();

builder.Services.AddMemoryCache();

// BDD-014: apply the same IsActive gate to SignalR hubs — shares the
// IDeviceAuthorizationService (and its IMemoryCache) with the MVC filter.
builder.Services.AddSignalR(options =>
{
    options.AddFilter<DeviceActiveHubFilter>();
});

// JWT Configuration
var jwtSettings = builder.Configuration.GetSection("Jwt").Get<JwtSettings>()!;
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("Jwt"));

// VAPID Configuration
builder.Services.Configure<VapidSettings>(builder.Configuration.GetSection("Vapid"));

// Supabase Configuration
builder.Services.Configure<SupabaseSettings>(builder.Configuration.GetSection("Supabase"));

// Email Configuration (Resend)
builder.Services.Configure<EmailSettings>(builder.Configuration.GetSection("Email"));

// Facturapi Configuration
builder.Services.Configure<FacturapiSettings>(builder.Configuration.GetSection("Facturapi"));
builder.Services.AddHttpClient<IFacturapiClient, FacturapiClient>((sp, client) =>
{
    var settings = builder.Configuration.GetSection("Facturapi").Get<FacturapiSettings>();
    var baseUrl = settings?.IsSandbox == false
        ? "https://www.facturapi.io/"
        : "https://www.facturapi.io/";
    client.BaseAddress = new Uri(baseUrl);
    if (!string.IsNullOrEmpty(settings?.ApiKey))
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.ApiKey);
});

// Stripe Configuration
builder.Services.Configure<StripeSettings>(builder.Configuration.GetSection("Stripe"));
var stripeSecretKey = builder.Configuration["Stripe:SecretKey"];
builder.Services.AddSingleton<IStripeClient>(new StripeClient(stripeSecretKey));

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.MapInboundClaims = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSettings.Issuer,
        ValidAudience = jwtSettings.Audience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Secret))
    };

    // SignalR clients cannot set Authorization headers on the WebSocket handshake,
    // so for any request under /hubs/ we accept the JWT from the access_token query string.
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];
            var path = context.HttpContext.Request.Path;
            if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
            {
                context.Token = accessToken;
            }
            return Task.CompletedTask;
        }
    };
});

// CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        var origins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>()
            ?? ["http://localhost:4200", "https://localhost:4200"];

        policy.WithOrigins(origins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

// Rate Limiting
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("RegistrationPolicy", limiter =>
    {
        limiter.PermitLimit = 5;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueLimit = 0;
    });
    options.AddFixedWindowLimiter("PublicInvoicingPolicy", limiter =>
    {
        limiter.PermitLimit = 5;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueLimit = 0;
    });
});

// Data Protection (used for encrypting delivery API keys)
builder.Services.AddDataProtection()
    .SetApplicationName("KajaPOS");

// Register dependencies
builder.Services.AddRepositoryDependencies(builder.Configuration);
builder.Services.AddServiceDependencies();

// Background workers
builder.Services.AddHostedService<StripeEventProcessorWorker>();
builder.Services.AddHostedService<PaymentWebhookProcessorWorker>();
builder.Services.AddHostedService<KdsEventDispatcherWorker>();

var app = builder.Build();

// Apply pending migrations automatically on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.Database.Migrate();

    await DbInitializer.SeedSystemDataAsync(db);

    if (app.Environment.IsDevelopment())
    {
        var encryptor = scope.ServiceProvider.GetRequiredService<POS.Services.Adapter.DataProtectionHelper>();
        await DbInitializer.SeedTestDataAsync(db, encryptor);
    }
}

// Enable request body buffering for Stripe webhook signature verification
app.Use((context, next) =>
{
    context.Request.EnableBuffering();
    return next();
});

// Middleware order matters — exception handler first
app.UseExceptionMiddleware();

app.UseSerilogRequestLogging();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI();
}

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseCors("AllowFrontend");

app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<KdsHub>("/hubs/kds");

app.Run();
