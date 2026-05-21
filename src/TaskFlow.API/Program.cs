using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Localization;
using Microsoft.OpenApi.Models;
using Serilog;
using TaskFlow.API.Authorization;
using TaskFlow.API.Localization;
using TaskFlow.API.Middleware;
using TaskFlow.API.Realtime;
using TaskFlow.Application;
using TaskFlow.Application.Common.Interfaces;
using TaskFlow.Infrastructure;
using TaskFlow.Infrastructure.Persistence.Seeding;

var builder = WebApplication.CreateBuilder(args);

// --- Logging (Serilog) ---
builder.Host.UseSerilog((ctx, cfg) => cfg.ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console());

// --- Layers ---
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// --- Web ---
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSingleton<IMessageLocalizer, MessageLocalizer>();

// --- Realtime (SignalR) — replace the no-op notifier with the real one ---
builder.Services.AddSignalR();
builder.Services.AddSingleton<IRealtimeNotifier, SignalRRealtimeNotifier>();

// RBAC: dynamic permission policies.
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddScoped<IAuthorizationHandler, PermissionHandler>();

// --- Bilingual request localization (en / ar) ---
var supportedCultures = new[] { new CultureInfo("en"), new CultureInfo("ar") };
builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    options.DefaultRequestCulture = new RequestCulture("en");
    options.SupportedCultures = supportedCultures;
    options.SupportedUICultures = supportedCultures;
    // Resolution order: ?culture=ar  ->  Accept-Language header.
    options.RequestCultureProviders =
    [
        new QueryStringRequestCultureProvider(),
        new AcceptLanguageHeaderRequestCultureProvider()
    ];
});

// --- Swagger with JWT bearer ---
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "TaskFlow API", Version = "v1" });
    var scheme = new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
    };
    c.AddSecurityDefinition("Bearer", scheme);
    c.AddSecurityRequirement(new OpenApiSecurityRequirement { [scheme] = Array.Empty<string>() });
});

// --- Rate limiting ---
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            ctx.User.Identity?.Name ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "anon",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 100, Window = TimeSpan.FromMinutes(1) }));
});

// --- CORS (web + mobile clients) ---
builder.Services.AddCors(options => options.AddPolicy("Default", p => p
    .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:5173"])
    .AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

// --- Health checks ---
builder.Services.AddHealthChecks();

var app = builder.Build();

// Localization must wrap the exception handler so error messages honor the
// request culture (CurrentUICulture is an AsyncLocal — it flows to children, not parents).
app.UseRequestLocalization();

app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseSerilogRequestLogging();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Serve the bilingual web frontend from wwwroot.
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseHttpsRedirection();
app.UseCors("Default");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<NotificationsHub>("/hubs/notifications");
app.MapHub<BoardHub>("/hubs/board");
app.MapHealthChecks("/health/live");
app.MapHealthChecks("/health/ready");

// Apply migrations + seed permissions at startup.
if (app.Configuration.GetValue("RunMigrationsOnStartup", true))
    await DbSeeder.SeedAsync(app.Services);

app.Run();
