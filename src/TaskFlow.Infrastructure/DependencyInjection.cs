using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using TaskFlow.Application.Common.Interfaces;
using TaskFlow.Application.Features.Accounting;
using TaskFlow.Application.Features.Auth;
using TaskFlow.Application.Features.Billing;
using TaskFlow.Application.Features.Files;
using TaskFlow.Application.Features.Integration;
using TaskFlow.Application.Features.Notifications;
using TaskFlow.Application.Features.Org;
using TaskFlow.Application.Features.Projects;
using TaskFlow.Application.Features.Recurring;
using TaskFlow.Application.Features.Reports;
using TaskFlow.Application.Features.Tasks;
using TaskFlow.Application.Features.TimeTracking;
using TaskFlow.Application.Features.Users;
using TaskFlow.Infrastructure.Persistence;
using TaskFlow.Infrastructure.Persistence.Interceptors;
using TaskFlow.Infrastructure.Services;

namespace TaskFlow.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<JwtOptions>(config.GetSection(JwtOptions.SectionName));

        services.AddScoped<AuditLogInterceptor>();
        services.AddScoped<AuditableSaveChangesInterceptor>();
        services.AddDbContext<AppDbContext>((sp, options) =>
        {
            options.UseSqlServer(
                config.GetConnectionString("Default"),
                sql => sql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName));
            // Audit interceptor runs FIRST so it sees true delete states before the
            // auditable interceptor rewrites soft-deletes into updates.
            options.AddInterceptors(
                sp.GetRequiredService<AuditLogInterceptor>(),
                sp.GetRequiredService<AuditableSaveChangesInterceptor>());
        });
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        services.AddHttpContextAccessor();
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddSingleton<IDateTime, SystemDateTime>();
        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddScoped<IJwtService, JwtService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IProjectService, ProjectService>();
        services.AddScoped<ITaskService, TaskService>();
        services.AddScoped<ITimeTrackingService, TimeTrackingService>();
        services.AddScoped<IFileService, FileService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<IActivityService, ActivityService>();
        services.AddScoped<IReportService, ReportService>();
        services.AddScoped<IReportExporter, ReportExporter>();
        services.AddScoped<IUserAdminService, UserAdminService>();
        services.AddScoped<IInvitationService, InvitationService>();
        services.AddScoped<TaskFlow.Application.Features.Audit.IAuditService, AuditQueryService>();

        // Paymo integration: encrypt API keys at rest; pick sandbox vs real HTTP client.
        // Persist the data-protection key ring to a stable folder so encrypted API keys
        // survive app restarts/recycles (otherwise the ring regenerates and decryption fails).
        var keysDir = config["DataProtection:KeysPath"]
            ?? Path.Combine(AppContext.BaseDirectory, "App_Data", "dp-keys");
        Directory.CreateDirectory(keysDir);
        services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(keysDir))
            .SetApplicationName("TaskFlow");
        services.AddSingleton<IKeyProtector, DataProtectionKeyProtector>();
        if (config.GetValue("Paymo:UseSandbox", true))
            services.AddScoped<IPaymoClient, Integration.Paymo.PaymoSandboxClient>();
        else
        {
            // Generous timeout: Paymo never paginates, so a busy project can return a large body.
            services.AddHttpClient<Integration.Paymo.PaymoHttpClient>(c => c.Timeout = TimeSpan.FromSeconds(120));
            services.AddScoped<IPaymoClient>(sp => sp.GetRequiredService<Integration.Paymo.PaymoHttpClient>());
        }
        services.AddScoped<IMigrationService, Integration.Paymo.MigrationService>();
        services.AddSingleton<IMigrationQueue, Integration.Paymo.MigrationQueue>();
        services.AddHostedService<Integration.Paymo.MigrationBackgroundService>();

        services.AddScoped<IRecurringTaskService, RecurringTaskService>();
        services.AddHostedService<RecurringTaskWorker>();

        services.AddSingleton<IPaymentProvider, PayPalSandboxProvider>();
        services.AddScoped<IBillingService, BillingService>();

        services.AddScoped<IClientService, ClientService>();
        services.AddScoped<IDepartmentAccess, DepartmentAccess>();
        services.AddScoped<IDepartmentService, DepartmentService>();
        services.AddScoped<ICategoryService, CategoryService>();
        services.AddScoped<IDiscussionService, DiscussionService>();
        services.AddHostedService<DeadlineReminderWorker>();

        services.AddScoped<IInvoiceService, InvoiceService>();
        services.AddScoped<IEstimateService, EstimateService>();
        services.AddScoped<IExpenseService, ExpenseService>();

        services.AddSingleton<IFileStorage, LocalFileStorage>();
        services.AddSingleton<IEmailSender, LoggingEmailSender>();
        // Realtime notifier: real SignalR implementation is registered by the API layer.
        services.AddSingleton<IRealtimeNotifier, NullRealtimeNotifier>();

        AddJwtAuthentication(services, config);

        return services;
    }

    private static void AddJwtAuthentication(IServiceCollection services, IConfiguration config)
    {
        var jwt = config.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();

        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = jwt.Issuer,
                ValidAudience = jwt.Audience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
                ClockSkew = TimeSpan.FromSeconds(30)
            };

            // WebSockets can't send an Authorization header — read the token from the
            // query string for SignalR hub connections under /hubs.
            options.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    var accessToken = context.Request.Query["access_token"];
                    var path = context.HttpContext.Request.Path;
                    if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                        context.Token = accessToken;
                    return Task.CompletedTask;
                }
            };
        });

        services.AddAuthorization();
    }
}
