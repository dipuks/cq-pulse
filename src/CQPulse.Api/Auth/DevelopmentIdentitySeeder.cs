using CQPulse.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;

namespace CQPulse.Api.Auth;

public static class DevelopmentIdentitySeeder
{
    public static async Task SeedAsync(IServiceProvider services, IConfiguration configuration, ILogger logger)
    {
        var email = configuration["DevelopmentAdmin:Email"];
        var password = configuration["DevelopmentAdmin:Password"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            logger.LogInformation("Development administrator not seeded: DevelopmentAdmin user secrets are not configured.");
            return;
        }

        var roles = services.GetRequiredService<RoleManager<IdentityRole>>();
        if (!await roles.RoleExistsAsync("Administrator")) await roles.CreateAsync(new IdentityRole("Administrator"));
        var users = services.GetRequiredService<UserManager<ApplicationUser>>();
        if (await users.FindByEmailAsync(email) is not null) return;

        var result = await users.CreateAsync(new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true, DisplayName = "Development Administrator" }, password);
        if (!result.Succeeded) throw new InvalidOperationException($"Unable to seed development administrator: {string.Join("; ", result.Errors.Select(x => x.Description))}");
        var user = await users.FindByEmailAsync(email) ?? throw new InvalidOperationException("Seeded administrator was not found.");
        await users.AddToRoleAsync(user, "Administrator");
    }
}
