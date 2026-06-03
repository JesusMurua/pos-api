using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.IdentityModel.Tokens;
using POS.API.Adapter;
using POS.API.Auth;
using POS.API.Filters;
using POS.API.Hubs;
using POS.API.Middleware;
using POS.API.Workers;
using Microsoft.AspNetCore.Authentication;
using POS.Domain.Settings;
using Microsoft.EntityFrameworkCore;
using Stripe;
using POS.Repository;
using POS.Repository.Dependencies;
using POS.Services.Adapter;
using POS.Services.Dependencies;
using POS.Services.IService;
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

var hmacSecret = Environment.GetEnvironmentVariable("ACCESS_CONTROL_QR_TOKEN_HMAC_SECRET");
if (!string.IsNullOrEmpty(hmacSecret))
    builder.Configuration["AccessControl:QrTokenHmacSecret"] = hmacSecret;

var adminApiToken = Environment.GetEnvironmentVariable("ADMIN_API_TOKEN");
if (!string.IsNullOrEmpty(adminApiToken))
    builder.Configuration["Admin:ApiToken"] = adminApiToken;

// Admin API Token fail-fast — opaque header secret for ops-only endpoints
// (catalog invalidation, future admin actions). Production requires a
// 32-character minimum (≥ 256-bit randomness) so a misconfigured deploy
// never silently accepts an empty or guessable token.
if (builder.Environment.IsProduction())
{
    var configuredAdminToken = builder.Configuration["Admin:ApiToken"];
    if (string.IsNullOrWhiteSpace(configuredAdminToken) || configuredAdminToken.Length < 32)
    {
        throw new InvalidOperationException(
            "ADMIN_API_TOKEN must be set in Production and be at least 32 characters " +
            "(≥256-bit random). It authenticates ops-only admin endpoints such as " +
            "POST /api/Admin/catalogs/invalidate.");
    }
}

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

// Access Control Configuration (gym/access-control cryptography secrets).
builder.Services.Configure<AccessControlSettings>(builder.Configuration.GetSection("AccessControl"));

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
})
// Secondary scheme for ops-only admin endpoints. Authenticated via opaque
// X-Admin-Token header (see AdminTokenAuthenticationHandler). Coexists with
// JWT Bearer — controllers opt in via
// [Authorize(AuthenticationSchemes = AdminTokenAuthenticationHandler.SchemeName)].
.AddScheme<AuthenticationSchemeOptions, AdminTokenAuthenticationHandler>(
    AdminTokenAuthenticationHandler.SchemeName, _ => { });

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

// Forwarded Headers — required so the rate limiter and request logging see
// the real client IP behind Render's reverse proxy. KnownNetworks/Proxies are
// cleared because Render does not expose stable proxy IPs; the container only
// receives traffic via the platform proxy, so trusting X-Forwarded-For
// unconditionally is acceptable.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

// Rate Limiting
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Surface Retry-After on every 429 globally so clients (especially the
    // device setup screen) can back off intelligently instead of guessing.
    // Logs the resolved IP and path for security observability — useful for
    // spotting brute-force patterns against /api/Device/activate.
    options.OnRejected = async (context, cancellationToken) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter =
                ((int)retryAfter.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        var ipAddress = context.HttpContext.Connection.RemoteIpAddress?.ToString() ?? IPAddress.None.ToString();
        Log.Warning("Rate limit hit for {IpAddress} on {Path}",
            ipAddress, context.HttpContext.Request.Path.Value);

        await ValueTask.CompletedTask;
    };

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

    // Sliding-window policy partitioned by real client IP. Sliding (vs fixed)
    // closes the burst-boundary loophole on an anti-bruteforce endpoint where
    // 10 requests at second 59 + 10 at second 61 would otherwise count as
    // "10 per minute" while behaving like 20-in-2-seconds.
    options.AddPolicy("DeviceActivationPolicy", httpContext =>
    {
        var partitionKey = httpContext.Connection.RemoteIpAddress?.ToString()
            ?? IPAddress.None.ToString();

        return RateLimitPartition.GetSlidingWindowLimiter(
            partitionKey,
            _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                SegmentsPerWindow = 6,
                QueueLimit = 0
            });
    });

    // Print dispatch policy. Partitioned by client IP because UseRateLimiter
    // runs before UseAuthentication in the pipeline, so User claims are not
    // yet populated when the policy lambda evaluates. 60 prints/minute per IP
    // protects the local hardware bridge from flood attacks without blocking
    // legitimate cashier traffic during peak hours.
    options.AddPolicy("PrintCommandPolicy", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? IPAddress.None.ToString(),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 60,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));

    // Catalog invalidate policy. Auth via X-Admin-Token already mitigates
    // unauthorized hits, but the policy bounds even an authorized client to
    // 10 invalidations per minute per IP so a runaway script cannot churn
    // the cache and turn every catalog read into a DB hit. Sliding window
    // closes the burst-boundary gap a fixed window would leave open.
    options.AddPolicy("CatalogInvalidatePolicy", httpContext =>
        RateLimitPartition.GetSlidingWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? IPAddress.None.ToString(),
            _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                SegmentsPerWindow = 6,
                QueueLimit = 0
            }));

    // Admin tenant directory policy. Same partition / window shape as the
    // catalog invalidator; the higher permit limit reflects that the admin
    // panel may legitimately poll the GET listing while the operator works
    // through a batch of demo setups. Auth via X-Admin-Token is the primary
    // mitigation — this policy is just a circuit breaker against runaway
    // scripts that grab the token.
    options.AddPolicy("AdminBusinessCreationPolicy", httpContext =>
        RateLimitPartition.GetSlidingWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? IPAddress.None.ToString(),
            _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromMinutes(1),
                SegmentsPerWindow = 6,
                QueueLimit = 0
            }));

    // POS first-time-setup orchestration policy. Each Owner / Manager triggers
    // a single InitializeCashierSession when their browser lands on /pos/sell;
    // the cap at 60 / minute / IP comfortably covers normal multi-branch flows
    // (operator clicking through several locations in a row) while still
    // acting as a circuit breaker against scripts that try to exhaust device
    // licensing quota by spamming uuids.
    options.AddPolicy("PosInitializePolicy", httpContext =>
        RateLimitPartition.GetSlidingWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? IPAddress.None.ToString(),
            _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = 60,
                Window = TimeSpan.FromMinutes(1),
                SegmentsPerWindow = 6,
                QueueLimit = 0
            }));
});

// Data Protection — persists keys to a directory configurable via the
// DATA_PROTECTION_KEYS_PATH env var. Without persistence, keys regenerate on
// every container restart, making any column cifrado con IDataProtector
// (BranchDeliveryConfig.ApiKeyEncrypted, Customer.BiometricTemplate, ...)
// permanently undecryptable after a redeploy. Production fail-fasts when the
// path is missing so a misconfigured deploy never reaches the request pipeline.
// WARNING: changing the application name invalidates every column already
// encrypted under the previous name ("KajaPOS"). On environments holding real
// encrypted data (BranchDeliveryConfig.ApiKeyEncrypted, BranchPaymentConfig.
// AccessToken, Customer.BiometricTemplate), re-encrypt those values before
// deploying this change — otherwise IDataProtector.Unprotect will fail.
var dataProtectionBuilder = builder.Services.AddDataProtection()
    .SetApplicationName("FinoPOS");

var keysPath = Environment.GetEnvironmentVariable("DATA_PROTECTION_KEYS_PATH");
if (string.IsNullOrEmpty(keysPath))
{
    if (builder.Environment.IsProduction())
    {
        throw new InvalidOperationException(
            "DATA_PROTECTION_KEYS_PATH must be set in Production. Mount a " +
            "persistent volume and point this variable at it, or every redeploy " +
            "will silently invalidate every encrypted column in the database.");
    }
}
else
{
    Directory.CreateDirectory(keysPath);
    dataProtectionBuilder.PersistKeysToFileSystem(new DirectoryInfo(keysPath));
}

// Register dependencies
builder.Services.AddRepositoryDependencies(builder.Configuration);
builder.Services.AddServiceDependencies();

// SignalR-backed adapter that lets POS.Services push commands to the bridge
// without taking a project reference back to POS.API.
builder.Services.AddSingleton<IBridgeNotifier, BridgeNotifier>();

// Background workers
builder.Services.AddHostedService<StripeEventProcessorWorker>();
builder.Services.AddHostedService<PaymentWebhookProcessorWorker>();
builder.Services.AddHostedService<KdsEventDispatcherWorker>();

var app = builder.Build();

// Apply pending migrations automatically on startup
using (var scope = app.Services.CreateScope())
{
    // Eagerly resolve cryptography singletons so any constructor fail-fast
    // (missing/short HMAC secret, etc.) trips the process before HTTP traffic
    // arrives — instead of surfacing as 500s on the first access-control call.
    _ = scope.ServiceProvider.GetRequiredService<IHmacService>();

    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    // Production (PostgreSQL) walks the migration history. The integration
    // test host runs on EF Core InMemory which is not relational, has no
    // migrations, and needs EnsureCreated to apply the HasData seeds baked
    // into OnModelCreating (Business 1, Branch 1, ...).
    if (db.Database.IsRelational())
    {
        db.Database.Migrate();
    }
    else
    {
        db.Database.EnsureCreated();
    }

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

// Forwarded headers MUST run before any middleware that reads the client IP
// (rate limiter, request logging) so partition keys and audit logs reflect the
// real caller, not the Render proxy.
app.UseForwardedHeaders();

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
app.MapHub<BridgeHub>("/hubs/bridge");

app.Run();

// Exposes the implicit top-level-statement Program type to
// WebApplicationFactory<Program> in POS.IntegrationTests.
public partial class Program { }
