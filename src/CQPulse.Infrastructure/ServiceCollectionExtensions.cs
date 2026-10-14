using CQPulse.Application.Simulation;
using CQPulse.Infrastructure.Identity;
using CQPulse.Infrastructure.Catalog;
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
        services.AddHostedService<SimulationWorker>();
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
}
