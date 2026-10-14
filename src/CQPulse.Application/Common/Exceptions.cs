namespace CQPulse.Application.Common;

/// <summary>Request input is invalid. Mapped to HTTP 400 with per-field errors.</summary>
public sealed class ValidationException(IReadOnlyDictionary<string, string[]> errors)
    : Exception("One or more validation errors occurred.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;

    public static ValidationException For(string field, string message) =>
        new(new Dictionary<string, string[]> { [field] = [message] });
}

/// <summary>A referenced resource does not exist. Mapped to HTTP 404.</summary>
public sealed class NotFoundException(string message) : Exception(message);

/// <summary>The request conflicts with current state (duplicates, terminal runs). Mapped to HTTP 409.</summary>
public sealed class ConflictException(string message) : Exception(message);

/// <summary>Collects field errors and throws a single <see cref="ValidationException"/>.</summary>
public sealed class ValidationErrors
{
    private readonly Dictionary<string, List<string>> _errors = new(StringComparer.OrdinalIgnoreCase);

    public ValidationErrors Add(string field, string message)
    {
        if (!_errors.TryGetValue(field, out var messages)) _errors[field] = messages = [];
        messages.Add(message);
        return this;
    }

    public ValidationErrors AddIf(bool condition, string field, string message) => condition ? Add(field, message) : this;

    public void ThrowIfAny()
    {
        if (_errors.Count > 0) throw new ValidationException(_errors.ToDictionary(x => x.Key, x => x.Value.ToArray()));
    }
}
