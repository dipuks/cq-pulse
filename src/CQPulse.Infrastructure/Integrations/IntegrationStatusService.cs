using CQPulse.Application.Analysis;
using CQPulse.Infrastructure.Integrations.LogTriage;
using Microsoft.Extensions.Options;

namespace CQPulse.Infrastructure.Integrations;

public interface IIntegrationStatusService
{
    Task<IReadOnlyList<IntegrationStatusDto>> CheckAsync(CancellationToken cancellationToken);
}

internal sealed class IntegrationStatusService(
    LogTriageClient logTriage,
    IOptions<LogTriageOptions> logTriageOptions) : IIntegrationStatusService
{
    public async Task<IReadOnlyList<IntegrationStatusDto>> CheckAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var options = logTriageOptions.Value;
        IntegrationStatusDto logTriageStatus;
        if (!options.IsConfigured)
        {
            logTriageStatus = new IntegrationStatusDto("LogTriageAssistant", false, "NotConfigured",
                "Set Integrations:LogTriage:BaseUrl, Username and Password (user secrets) to enable failure analysis.", null, now);
        }
        else
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            var (healthy, detail) = await logTriage.CheckReadinessAsync(timeout.Token);
            logTriageStatus = new IntegrationStatusDto("LogTriageAssistant", true, healthy ? "Healthy" : "Unavailable", detail, options.BaseUrl, now);
        }

        return
        [
            logTriageStatus,
            new IntegrationStatusDto("SmartDog", false, "NotIntegrated",
                "SmartDog has no structured decision endpoint or ranking yet; see docs/quality-intelligence-architecture.md.", null, now)
        ];
    }
}
