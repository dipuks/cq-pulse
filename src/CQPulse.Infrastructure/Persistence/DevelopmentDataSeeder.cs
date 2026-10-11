using CQPulse.Domain;
using CQPulse.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CQPulse.Infrastructure.Persistence;

public static class DevelopmentDataSeeder
{
    public static async Task SeedAsync(CQPulseDbContext dbContext, CancellationToken cancellationToken)
    {
        if (!await dbContext.Projects.AnyAsync(cancellationToken))
        {
            dbContext.Projects.Add(new Project { Name = "Web Portal", Description = "Customer self-service web application" });
            dbContext.Projects.Add(new Project { Name = "Mobile App", Description = "iOS and Android customer application" });
            dbContext.Projects.Add(new Project { Name = "API Services", Description = "Internal platform APIs" });
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        if (await dbContext.TestSuites.AnyAsync(cancellationToken)) return;

        var project = await dbContext.Projects.OrderBy(x => x.Name).FirstAsync(cancellationToken);
        var suite = new TestSuite
        {
            ProjectId = project.Id,
            Name = "Login & Authentication",
            Description = "Core sign-in and account recovery scenarios"
        };
        suite.TestCases.Add(new TestCase
        {
            Name = "Valid login",
            Description = "A registered customer can sign in.",
            Priority = TestPriority.High,
            SimulatedOutcome = SimulatedOutcome.Passed,
            SimulatedDurationMs = 800
        });
        suite.TestCases.Add(new TestCase
        {
            Name = "Invalid password",
            Description = "Invalid credentials display a useful error.",
            Priority = TestPriority.High,
            SimulatedOutcome = SimulatedOutcome.Failed,
            SimulatedDurationMs = 650
        });
        dbContext.TestSuites.Add(suite);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
