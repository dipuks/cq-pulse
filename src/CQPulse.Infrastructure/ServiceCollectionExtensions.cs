using CQPulse.Application.Analysis;
using CQPulse.Application.Simulation;
using CQPulse.Infrastructure.Identity;
using CQPulse.Infrastructure.Catalog;
using CQPulse.Infrastructure.Integrations.LogTriage;
using CQPulse.Infrastructure.Integrations.SmartDog;
using Microsoft.Extensions.Http.Resilience;
using CQPulse.Infrastructure.Persistence;
using CQPulse.Infrastructure.Simulator;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CQPulse.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCQPulseInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("CQPulse");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:CQPulse must be configured before starting the API.");
        }

        services.AddDbContext<CQPulseDbContext>(options => options.UseSqlServer(connectionString));
        services.AddOptions<SimulatorOptions>()
            .Bind(configuration.GetSection(SimulatorOptions.SectionName))
            .Validate(options => !options.Validate().Any(), "Simulator configuration is invalid.")
            .ValidateOnStart();
        services.AddSingleton<SimulationQueue>();
        services.AddSingleton<RunCancellationRegistry>();
        services.AddSingleton<IResultBatchWriter, EfResultBatchWriter>();
        services.AddScoped<CQPulse.Application.Catalog.ICatalogService, CatalogService>();
        services.AddScoped<ISimulationService, SimulationService>();
        services.AddHostedService<ExecutionRecoveryService>();
        services.AddOptions<SignatureBackfillOptions>()
            .Bind(configuration.GetSection(SignatureBackfillOptions.SectionName))
            .Validate(options => options.IsValid, "Insights:SignatureBackfill is invalid: BatchSize must be 1-2,000 and PauseMilliseconds 0-60,000.")
            .ValidateOnStart();
        services.AddSingleton<FailureSignatureBackfill>();
        services.AddHostedService<SignatureBackfillService>();
        services.AddHostedService<SimulationWorker>();
        AddLogTriageIntegration(services, configuration);
        AddSmartDogIntegration(services, configuration);
        services.AddScoped<CQPulse.Infrastructure.Integrations.IIntegrationStatusService, CQPulse.Infrastructure.Integrations.IntegrationStatusService>();
        services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.Password.RequiredLength = 12;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = true;
                options.User.RequireUniqueEmail = true;
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
            })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<CQPulseDbContext>();

        return services;
    }

    private static void AddSmartDogIntegration(IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection(SmartDogOptions.SectionName).Get<SmartDogOptions>() ?? new SmartDogOptions();
        services.AddOptions<SmartDogOptions>().Bind(configuration.GetSection(SmartDogOptions.SectionName));
        services.AddScoped<CQPulse.Application.Decisions.IDecisionSupportService, DecisionSupportService>();

        var attemptTimeout = TimeSpan.FromSeconds(Math.Max(1, settings.AttemptTimeoutSeconds));
        services.AddHttpClient<SmartDogClient>(client =>
            {
                if (Uri.TryCreate(settings.BaseUrl, UriKind.Absolute, out var baseUrl))
                    client.BaseAddress = new Uri(baseUrl.ToString().TrimEnd('/') + "/");
                client.Timeout = Timeout.InfiniteTimeSpan; // the resilience pipeline owns timeouts
            })
            // Evaluation is stateless, so retrying transient failures (network, 408, 429, 5xx) cannot duplicate side effects.
            .AddStandardResilienceHandler(resilience =>
            {
                resilience.AttemptTimeout.Timeout = attemptTimeout;
                resilience.Retry.MaxRetryAttempts = Math.Max(0, settings.RetryCount);
                resilience.Retry.Delay = TimeSpan.FromMilliseconds(Math.Max(1, settings.RetryBaseDelayMilliseconds));
                resilience.Retry.UseJitter = true;
                resilience.TotalRequestTimeout.Timeout = attemptTimeout * (settings.RetryCount + 2);
                resilience.CircuitBreaker.SamplingDuration = attemptTimeout * 2;
                resilience.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(15);
            });
    }

    private static void AddLogTriageIntegration(IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection(LogTriageOptions.SectionName).Get<LogTriageOptions>() ?? new LogTriageOptions();
        services.AddOptions<LogTriageOptions>().Bind(configuration.GetSection(LogTriageOptions.SectionName))
            .Validate(options => options.IsValid,
                $"Integrations:LogTriage is invalid: MaxFailures must be 1-{LogTriageOptions.ProviderFindingLimit} (LogTriageAssistant reports findings for at most {LogTriageOptions.ProviderFindingLimit} lines), MaxDocumentBytes positive, QueueCapacity 1-10,000 and PendingSweepSeconds 1-86,400.")
            .ValidateOnStart();
        services.AddSingleton<LogTriageSession>();
        services.AddSingleton<AnalysisQueue>();
        services.AddScoped<FailureAnalysisService>();
        services.AddScoped<IFailureAnalysisService>(provider => provider.GetRequiredService<FailureAnalysisService>());
        services.AddScoped<IRunCompletionObserver>(provider => provider.GetRequiredService<FailureAnalysisService>());
        services.AddHostedService<AnalysisWorker>();

        var attemptTimeout = TimeSpan.FromSeconds(Math.Max(1, settings.AttemptTimeoutSeconds));
        services.AddHttpClient<LogTriageClient>(client =>
            {
                if (Uri.TryCreate(settings.BaseUrl, UriKind.Absolute, out var baseUrl))
                    client.BaseAddress = new Uri(baseUrl.ToString().TrimEnd('/') + "/");
                client.Timeout = Timeout.InfiniteTimeSpan; // the resilience pipeline owns timeouts
            })
            // The handler keeps LogTriageAssistant's session cookie, so it is not recycled; connections still are.
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                UseCookies = true,
                CookieContainer = new System.Net.CookieContainer(),
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            })
            .SetHandlerLifetime(Timeout.InfiniteTimeSpan)
            .AddStandardResilienceHandler(resilience =>
            {
                resilience.AttemptTimeout.Timeout = attemptTimeout;
                resilience.Retry.MaxRetryAttempts = Math.Max(0, settings.RetryCount);
                resilience.Retry.Delay = TimeSpan.FromMilliseconds(Math.Max(1, settings.RetryBaseDelayMilliseconds));
                resilience.Retry.UseJitter = true;
                resilience.TotalRequestTimeout.Timeout = attemptTimeout * (settings.RetryCount + 2);
                resilience.CircuitBreaker.SamplingDuration = attemptTimeout * 2;
                resilience.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(15);
            });
    }
}
