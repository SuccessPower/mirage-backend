using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Mirage.Api;
using Mirage.Api.Endpoints;
using Mirage.Api.Hubs;
using Mirage.Api.Middleware;
using Mirage.Api.Security;
using Mirage.Api.Services;
using Mirage.Infrastructure.Identity;
using Mirage.Application;
using Mirage.Application.Abstractions;
using Mirage.Infrastructure;
using Mirage.Infrastructure.Email;
using Mirage.Infrastructure.Persistence;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

var builder = WebApplication.CreateBuilder(args);
var isMigrationCommand = args.Contains("--migrate", StringComparer.OrdinalIgnoreCase);

builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

builder.Host.UseSerilog((context, services, loggerConfiguration) =>
{
    loggerConfiguration
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithMachineName()
        .Enrich.WithEnvironmentName()
        .Enrich.WithThreadId()
        .Enrich.WithProperty("Application", "Mirage.Api")
        .Enrich.WithProperty("Service", "mirage-api");

    if (context.Configuration.GetSection("Serilog").Exists())
    {
        loggerConfiguration.ReadFrom.Configuration(context.Configuration);
        return;
    }

    loggerConfiguration
        .MinimumLevel.Information()
        .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
        .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
        .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Migrations", LogEventLevel.Information);

    if (context.HostingEnvironment.IsDevelopment())
    {
        loggerConfiguration
            .MinimumLevel.Debug()
            .WriteTo.Console(outputTemplate:
                "[{Timestamp:HH:mm:ss.fff} {Level:u3}] [{CorrelationId}] {SourceContext}: {Message:lj}{NewLine}{Exception}");
    }
    else
    {
        loggerConfiguration.WriteTo.Console(new RenderedCompactJsonFormatter());
    }
});

if (builder.Configuration["PORT"] is { Length: > 0 } port)
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

var missingConfiguration = new List<string>();
if (string.IsNullOrWhiteSpace(builder.Configuration["DATABASE_URL"]) &&
    string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("Postgres")))
{
    missingConfiguration.Add("DATABASE_URL");
}

var configuredSigningKey = builder.Configuration["Jwt:SigningKey"];
if (string.IsNullOrWhiteSpace(configuredSigningKey) || Encoding.UTF8.GetByteCount(configuredSigningKey) < 32)
{
    missingConfiguration.Add("Jwt__SigningKey (minimum 32 bytes)");
}

if (missingConfiguration.Count > 0)
{
    throw new InvalidOperationException(
        $"Missing or invalid required configuration: {string.Join(", ", missingConfiguration)}. " +
        "Set these as environment variables or in appsettings.Local.json (gitignored).");
}

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: true)));

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddMemoryCache();
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<JitsiService>();
builder.Services.AddScoped<WelcomeEmailBackfillService>();
builder.Services.AddHostedService<WelcomeEmailBackfillWorker>();
builder.Services.AddHttpClient<PaystackService>();
builder.Services.AddHttpClient<FlutterwaveService>();
builder.Services.AddHttpClient<IEmailService, MailjetSmtpEmailService>();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddHealthChecks()
    .AddDbContextCheck<MirageDbContext>("postgres", tags: ["ready"]);

var jwt = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
if (Encoding.UTF8.GetByteCount(jwt.SigningKey) < 32)
{
    throw new InvalidOperationException("Jwt:SigningKey must be at least 32 bytes.");
}

builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
    options.MaximumReceiveMessageSize = 8 * 1024; // 8 KB cap per message
})
    // Match the REST API's enum serialization (string names) so hub payloads like
    // Message.Type / DateRequest.Category are consistent across both transports.
    .AddJsonProtocol(options =>
        options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: true)));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "sub",
            RoleClaimType = "role"
        };
        options.Events = new JwtBearerEvents
        {
            // SignalR WebSocket connections send the JWT in the query string
            // because browsers cannot set custom headers on WebSocket upgrades.
            OnMessageReceived = context =>
            {
                var token = context.Request.Query["access_token"].ToString();
                if (!string.IsNullOrEmpty(token) &&
                    context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                {
                    context.Token = token;
                }
                return Task.CompletedTask;
            },
            OnTokenValidated = context =>
            {
                var logger = context.HttpContext.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("Mirage.Security.Authentication");
                logger.LogDebug(
                    "JWT validated for UserId {UserId}. CorrelationId: {CorrelationId}",
                    context.Principal?.FindFirst("sub")?.Value,
                    context.HttpContext.Items[CorrelationIdMiddleware.ItemKey]);
                return Task.CompletedTask;
            },
            OnAuthenticationFailed = context =>
            {
                var logger = context.HttpContext.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("Mirage.Security.Authentication");
                logger.LogWarning(
                    "JWT authentication failed for {RequestMethod} {RequestPath}. FailureType: {FailureType}; " +
                    "CorrelationId: {CorrelationId}",
                    context.Request.Method,
                    context.Request.Path,
                    context.Exception.GetType().Name,
                    context.HttpContext.Items[CorrelationIdMiddleware.ItemKey]);
                return Task.CompletedTask;
            },
            OnChallenge = context =>
            {
                var logger = context.HttpContext.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("Mirage.Security.Authentication");
                logger.LogWarning(
                    "Authentication challenge issued for {RequestMethod} {RequestPath}. " +
                    "CorrelationId: {CorrelationId}",
                    context.Request.Method,
                    context.Request.Path,
                    context.HttpContext.Items[CorrelationIdMiddleware.ItemKey]);
                return Task.CompletedTask;
            },
            OnForbidden = context =>
            {
                var logger = context.HttpContext.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("Mirage.Security.Authorization");
                logger.LogWarning(
                    "Authorization denied for UserId {UserId} on {RequestMethod} {RequestPath}. " +
                    "CorrelationId: {CorrelationId}",
                    context.Principal?.FindFirst("sub")?.Value,
                    context.Request.Method,
                    context.Request.Path,
                    context.HttpContext.Items[CorrelationIdMiddleware.ItemKey]);
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(MiragePolicy.PlatformAdmin,
        p => p.RequireRole(MirageRoles.PlatformAdmin));
    options.AddPolicy(MiragePolicy.ChurchAdmin,
        p => p.RequireRole(MirageRoles.ChurchAdmin, MirageRoles.PlatformAdmin));
    options.AddPolicy(MiragePolicy.Counsellor,
        p => p.RequireRole(MirageRoles.Counsellor, MirageRoles.ChurchAdmin, MirageRoles.PlatformAdmin));
    options.AddPolicy(MiragePolicy.Mentor,
        p => p.RequireRole(MirageRoles.Mentor, MirageRoles.PlatformAdmin));
    options.AddPolicy(MiragePolicy.Vendor,
        p => p.RequireRole(MirageRoles.Vendor, MirageRoles.PlatformAdmin));
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = (context, _) =>
    {
        var logger = context.HttpContext.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("Mirage.Api.RateLimiting");
        logger.LogWarning(
            "Rate limit exceeded for {RequestMethod} {RequestPath}. UserId: {UserId}; " +
            "RemoteIpAddress: {RemoteIpAddress}; CorrelationId: {CorrelationId}",
            context.HttpContext.Request.Method,
            context.HttpContext.Request.Path,
            context.HttpContext.User.FindFirst("sub")?.Value,
            context.HttpContext.Connection.RemoteIpAddress?.ToString(),
            context.HttpContext.Items[CorrelationIdMiddleware.ItemKey]);
        return ValueTask.CompletedTask;
    };
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.User.Identity?.Name ?? context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 120,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("MirageFrontend", policy =>
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Mirage API",
        Version = "v1",
        Description = "Backend API for the Mirage relationship platform.",
        License = new OpenApiLicense { Name = "MIT" }
    });
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        [new OpenApiSecurityScheme
        {
            Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
        }] = []
    });
});

var app = builder.Build();
var swaggerEnabled = app.Configuration.GetValue("Swagger:Enabled", true);

if (isMigrationCommand)
{
    await app.InitialiseDatabaseAsync(forceMigrations: true);
    return;
}

var forwardedHeaders = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
    ForwardLimit = 2
};
forwardedHeaders.KnownNetworks.Clear();
forwardedHeaders.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedHeaders);
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<ResponseTimeMiddleware>();
app.UseSerilogRequestLogging(options =>
{
    options.MessageTemplate =
        "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
    options.GetLevel = (httpContext, elapsed, exception) =>
    {
        if (exception is not null || httpContext.Response.StatusCode >= 500)
            return LogEventLevel.Error;
        if (httpContext.Response.StatusCode >= 400 || elapsed > 2_000)
            return LogEventLevel.Warning;
        return LogEventLevel.Information;
    };
    options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
    {
        diagnosticContext.Set("CorrelationId",
            httpContext.Items[CorrelationIdMiddleware.ItemKey]?.ToString() ?? string.Empty);
        diagnosticContext.Set("TraceId", httpContext.TraceIdentifier);
        diagnosticContext.Set("RequestHost", httpContext.Request.Host.Value);
        diagnosticContext.Set("RequestScheme", httpContext.Request.Scheme);
        diagnosticContext.Set("RequestProtocol", httpContext.Request.Protocol);
        diagnosticContext.Set("RemoteIpAddress", httpContext.Connection.RemoteIpAddress?.ToString());
        diagnosticContext.Set("UserAgent", httpContext.Request.Headers.UserAgent.ToString());
        diagnosticContext.Set("UserId", httpContext.User.FindFirst("sub")?.Value);
        diagnosticContext.Set("EndpointName", httpContext.GetEndpoint()?.DisplayName);
    };
});
app.UseExceptionHandler();
app.UseStatusCodePages();
if (swaggerEnabled)
{
    app.UseStaticFiles();
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.DocumentTitle = "Mirage API";
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Mirage API v1");
        options.InjectStylesheet("/swagger-ui/theme.css?v=2");
        options.InjectJavascript("/swagger-ui/theme.js?v=2");
        options.DisplayRequestDuration();
        options.EnableDeepLinking();
        options.EnablePersistAuthorization();
        options.DocExpansion(Swashbuckle.AspNetCore.SwaggerUI.DocExpansion.List);
    });
}

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}
app.UseCors("MirageFrontend");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });
app.MapHub<ChatHub>("/hubs/chat");
app.MapHub<NotificationHub>("/hubs/notifications");
app.MapMirageEndpoints();
if (swaggerEnabled)
{
    app.MapGet("/", () => Results.Redirect("/swagger/index.html", permanent: false))
        .ExcludeFromDescription();
}

await app.InitialiseDatabaseAsync();
await app.WarmDatabaseCachesAsync();
await app.RunAsync();

public partial class Program;
