using CQPulse.Application.Analysis;
using CQPulse.Infrastructure.Integrations.LogTriage;
using CQPulse.Infrastructure.Integrations.SmartDog;
using Microsoft.Extensions.Options;

namespace CQPulse.Infrastructure.Integrations;

public interface IIntegrationStatusService
{
    Task<IReadOnlyList<IntegrationStatusDto>> CheckAsync(CancellationToken cancellationToken);
}

/// <summary>Configuration and live health of each optional service, checked in parallel with a short timeout.</summary>
internal sealed class IntegrationStatusService(
    LogTriageClient logTriage,
    IOptions<LogTriageOptions> logTriageOptions,
    SmartDogClient smartDog,
    IOptions<SmartDogOptions> smartDogOptions) : IIntegrationStatusService
{
    public async Task<IReadOnlyList<IntegrationStatusDto>> CheckAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));

        var logTriageCheck = logTriageOptions.Value.IsConfigured
            ? Check("LogTriageAssistant", logTriageOptions.Value.BaseUrl, () => logTriage.CheckReadinessAsync(timeout.Token))
            : Task.FromResult(NotConfigured("LogTriageAssistant",
                "Set Integrations:LogTriage:BaseUrl, Username and Password (user secrets) to enable failure analysis."));
        var smartDogCheck = smartDogOptions.Value.IsConfigured
            ? Check("SmartDog", smartDogOptions.Value.BaseUrl, () => smartDog.CheckHealthAsync(timeout.Token))
            : Task.FromResult(NotConfigured("SmartDog",
                "Set Integrations:SmartDog:BaseUrl and ApiKey (user secrets) to enable decision support."));

        return await Task.WhenAll(logTriageCheck, smartDogCheck);
    }

    private static async Task<IntegrationStatusDto> Check(string name, string? baseUrl, Func<Task<(bool Healthy, string Detail)>> probe)
    {
        var (healthy, detail) = await probe();
        return new IntegrationStatusDto(name, true, healthy ? "Healthy" : "Unavailable", detail, baseUrl, DateTimeOffset.UtcNow);
    }

    private static IntegrationStatusDto NotConfigured(string name, string detail) =>
        new(name, false, "NotConfigured", detail, null, DateTimeOffset.UtcNow);
}

internal static class ProbeDetail
{
    /// <summary>Status-check detail for a failed probe.</summary>
    public static string Unreachable(Exception exception) => $"Unreachable: {Reason(exception)}";

    /// <summary>
    /// Why a call failed. Timeouts surface as cancellations whose own message ("A task was canceled.") says nothing, so
    /// they are named as timeouts; other failures keep the transport's message.
    /// </summary>
    public static string Reason(Exception exception) =>
        exception is OperationCanceledException or TimeoutException or Polly.Timeout.TimeoutRejectedException ||
        exception.GetBaseException() is TimeoutException or Polly.Timeout.TimeoutRejectedException
            ? "timed out waiting for a response."
            : exception.GetBaseException().Message;
}
