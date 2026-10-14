using CQPulse.Application.Simulation;
using CQPulse.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CQPulse.Infrastructure.Simulator;

/// <summary>
/// Loads the enabled catalog test cases a catalog-mode run executes, in a stable order (suite name, case name, id).
/// Preview and execution use this same query, so the preview's exact totals match the run.
/// </summary>
internal static class CatalogWorkloadLoader
{
    public static Task<List<CatalogCase>> LoadAsync(
        CQPulseDbContext dbContext, Guid projectId, Guid? testSuiteId, CancellationToken cancellationToken)
    {
        var query = dbContext.TestCases.AsNoTracking()
            .Where(x => x.Enabled && x.TestSuite.ProjectId == projectId);
        if (testSuiteId is not null) query = query.Where(x => x.TestSuiteId == testSuiteId);
        return query
            .OrderBy(x => x.TestSuite.Name).ThenBy(x => x.Name).ThenBy(x => x.Id)
            .Select(x => new CatalogCase(x.Id, x.Name, x.SimulatedOutcome, x.SimulatedDurationMs))
            .ToListAsync(cancellationToken);
    }
}
