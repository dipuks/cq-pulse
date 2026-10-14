namespace CQPulse.Application.Common;

/// <summary>The correlation id of the current request (or a new id outside of a request).</summary>
public interface ICorrelationContext
{
    string CorrelationId { get; }
}
