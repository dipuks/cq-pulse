using System.Text.Json;
using CQPulse.Application.Common;
using CQPulse.Application.Decisions;
using CQPulse.Domain;
using CQPulse.Domain.Entities;
using CQPulse.Infrastructure.Integrations.LogTriage;
using CQPulse.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CQPulse.Infrastructure.Integrations.SmartDog;

/// <summary>
/// Ranks a finished run's failure signatures with SmartDog. The evaluation is synchronous (SmartDog is stateless and
/// fast) with bounded timeouts and retries, and one record is stored per request with the submitted evidence and the
/// response or the reason it failed. CQ Pulse workflows do not depend on it.
/// </summary>
internal sealed class DecisionSupportService(
    CQPulseDbContext dbContext,
    IServiceProvider services,
    ICorrelationContext correlation,
    IOptions<SmartDogOptions> smartDogOptions,
    IOptions<LogTriageOptions> logTriageOptions,
    ILogger<DecisionSupportService> logger) : IDecisionSupportService
{
    public const string ProviderName = "SmartDog";
    public const string Question = "Which failure signature in this run should be investigated first?";
    private const int MaxErrorLength = 2_000;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<DecisionEvaluationDto> EvaluateRunAsync(
        Guid runId, DecisionSupportRequest? request, string? userId, CancellationToken cancellationToken)
    {
        var weights = ValidateAndResolveWeights(request);
        var minimumOccurrences = request?.MinimumOccurrences ?? 1;

        var run = await dbContext.TestRuns.AsNoTracking().Where(x => x.Id == runId)
            .Select(x => new { x.ProjectId, x.Status, x.FailedCount }).SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("The requested run does not exist.");
        if (!run.Status.IsTerminal()) throw new ConflictException("The run is still active; rank its failures after it finishes.");
        if (run.FailedCount == 0) throw new ConflictException("The run has no failed results to rank.");

        var evidence = await DecisionEvidenceBuilder.BuildAsync(
            dbContext, runId, run.ProjectId, logTriageOptions.Value, DateTimeOffset.UtcNow, cancellationToken);
        var notes = evidence.Notes.ToList();
        var criteria = DecisionCriteria.All
            .Where(id => id != DecisionCriteria.TriageSeverity || evidence.SeverityAvailable)
            .Select(id => new DecisionCriterionDto(id, DecisionCriteria.Names[id], weights[id], "maximize", DecisionCriteria.Sources[id]))
            .ToList();
        if (!evidence.SeverityAvailable && request?.Weights?.ContainsKey(DecisionCriteria.TriageSeverity) == true)
            notes.Add("The weight given to triage severity was not applied because severity evidence is unavailable.");

        var smartDogRequest = new
        {
            question = Question,
            criteria = criteria.Select(c => new { id = c.Id, name = c.Name, weight = c.Weight, direction = c.Direction }),
            options = evidence.Options.Select(o => new
            {
                id = o.OptionId,
                name = OptionName(o),
                values = criteria.ToDictionary(c => c.Id, c => ValueOf(o, c.Id))
            }),
            constraints = minimumOccurrences > 1
                ? new[] { new { id = "minimum-occurrences", criterionId = DecisionCriteria.OccurrencesInRun, @operator = ">=", value = (double)minimumOccurrences } }
                : []
        };

        var evaluation = new DecisionEvaluation
        {
            TestRunId = runId,
            ProjectId = run.ProjectId,
            Provider = ProviderName,
            CorrelationId = correlation.CorrelationId,
            RequestedByUserId = userId,
            Question = Question,
            RequestJson = JsonSerializer.Serialize(smartDogRequest, Json),
            EvidenceJson = JsonSerializer.Serialize(evidence.Options, Json),
            EvidenceNotesJson = JsonSerializer.Serialize(notes, Json),
            OptionCount = evidence.Options.Count,
            FailureAnalysisId = evidence.FailureAnalysisId
        };

        if (!smartDogOptions.Value.IsConfigured)
        {
            evaluation.Fail(DateTimeOffset.UtcNow, AnalysisStatus.Unavailable, "SmartDog is not configured (Integrations:SmartDog BaseUrl and ApiKey).");
        }
        else
        {
            var client = services.GetRequiredService<SmartDogClient>();
            try
            {
                var (raw, result) = await client.EvaluateAsync(smartDogRequest, evaluation.CorrelationId, cancellationToken);
                evaluation.Succeed(DateTimeOffset.UtcNow, result.EvaluationId, raw);
            }
            catch (SmartDogUnavailableException exception)
            {
                logger.LogWarning(exception, "Decision evaluation for run {RunId} could not reach SmartDog.", runId);
                evaluation.Fail(DateTimeOffset.UtcNow, AnalysisStatus.Unavailable, Truncate(exception.Message));
            }
            catch (SmartDogContractException exception)
            {
                logger.LogWarning("Decision evaluation for run {RunId} rejected: {Reason}", runId, exception.Message);
                evaluation.Fail(DateTimeOffset.UtcNow, AnalysisStatus.Failed, Truncate(exception.Message));
            }
        }

        dbContext.DecisionEvaluations.Add(evaluation);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        logger.LogInformation("Decision evaluation {EvaluationId} for run {RunId}: {Status}, {Options} options, severity {SeverityUsed}.",
            evaluation.Id, runId, evaluation.Status, evaluation.OptionCount, evidence.SeverityAvailable ? "used" : "not used");
        return ToDto(evaluation);
    }

    public async Task<IReadOnlyList<DecisionEvaluationDto>> GetForRunAsync(Guid runId, CancellationToken cancellationToken)
    {
        var evaluations = await dbContext.DecisionEvaluations.AsNoTracking().Where(x => x.TestRunId == runId)
            .OrderByDescending(x => x.RequestedAtUtc).Take(50).ToListAsync(cancellationToken);
        return evaluations.Select(ToDto).ToList();
    }

    public async Task<DecisionEvaluationDto?> GetAsync(Guid evaluationId, CancellationToken cancellationToken) =>
        await dbContext.DecisionEvaluations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == evaluationId, cancellationToken) is { } evaluation
            ? ToDto(evaluation)
            : null;

    private static Dictionary<string, double> ValidateAndResolveWeights(DecisionSupportRequest? request)
    {
        var errors = new ValidationErrors();
        var weights = new Dictionary<string, double>(DecisionCriteria.DefaultWeights);
        foreach (var (id, weight) in request?.Weights ?? new Dictionary<string, double>())
        {
            if (!DecisionCriteria.All.Contains(id)) errors.Add("Weights", $"Unknown criterion '{id}'. Use: {string.Join(", ", DecisionCriteria.All)}.");
            else if (!double.IsFinite(weight) || weight <= 0 || weight > 1_000) errors.Add("Weights", $"Weight for '{id}' must be greater than 0 and at most 1000.");
            else weights[id] = weight;
        }
        errors.AddIf(request?.MinimumOccurrences is < 1 or > 1_000_000, "MinimumOccurrences", "Minimum occurrences must be between 1 and 1,000,000.");
        errors.ThrowIfAny();
        return weights;
    }

    private static double ValueOf(DecisionOptionEvidenceDto option, string criterionId) => criterionId switch
    {
        DecisionCriteria.OccurrencesInRun => option.OccurrencesInRun,
        DecisionCriteria.Recurrence30Days => option.Recurrence30Days,
        DecisionCriteria.AffectedTestCases => option.AffectedTestCases,
        DecisionCriteria.TriageSeverity => SeverityScale.Rank(option.TriageSeverity)
            ?? throw new InvalidOperationException("Triage severity is only submitted when every option has one."),
        _ => throw new ArgumentOutOfRangeException(nameof(criterionId))
    };

    private static string OptionName(DecisionOptionEvidenceDto option)
    {
        var name = $"{option.Category?.ToString() ?? "Uncategorized"}: {option.NormalizedMessage}";
        return name.Length <= 200 ? name : name[..200];
    }

    private static string Truncate(string text) => text.Length <= MaxErrorLength ? text : text[..MaxErrorLength];

    internal static DecisionEvaluationDto ToDto(DecisionEvaluation x)
    {
        var request = Deserialize<StoredRequest>(x.RequestJson);
        var criteria = (request?.Criteria ?? []).Select(c => new DecisionCriterionDto(
            c.Id, c.Name ?? c.Id, c.Weight, c.Direction ?? "maximize", DecisionCriteria.Sources.GetValueOrDefault(c.Id, ""))).ToList();
        var minimum = request?.Constraints?.FirstOrDefault(c => c.Id == "minimum-occurrences")?.Value;
        return new DecisionEvaluationDto(
            x.Id, x.TestRunId, x.Provider, x.Status, x.RequestedAtUtc, x.CompletedAtUtc, x.CorrelationId, x.Question, criteria,
            minimum is null ? null : (int)minimum.Value,
            Deserialize<List<DecisionOptionEvidenceDto>>(x.EvidenceJson) ?? [], Deserialize<List<string>>(x.EvidenceNotesJson) ?? [],
            x.FailureAnalysisId, x.ExternalEvaluationId, Deserialize<DecisionResultDto>(x.ResultJson), x.LastError);
    }

    private static T? Deserialize<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<T>(json, Json); } catch (JsonException) { return null; }
    }

    private sealed record StoredRequest(List<StoredCriterion>? Criteria, List<StoredConstraint>? Constraints);
    private sealed record StoredCriterion(string Id, string? Name, double Weight, string? Direction);
    private sealed record StoredConstraint(string? Id, string? CriterionId, string? Operator, double? Value);
}
