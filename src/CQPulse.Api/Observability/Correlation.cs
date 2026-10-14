using System.Text.RegularExpressions;
using CQPulse.Application.Common;

namespace CQPulse.Api.Observability;

/// <summary>
/// Accepts a well-formed incoming X-Correlation-ID (or creates one), echoes it on the response, and adds it to the
/// logging scope. Background work stores the id on the records it creates and forwards it to external services.
/// </summary>
public sealed partial class CorrelationMiddleware(RequestDelegate next, ILogger<CorrelationMiddleware> logger)
{
    public const string HeaderName = "X-Correlation-ID";
    public const string ItemKey = "CorrelationId";

    [GeneratedRegex("^[A-Za-z0-9._-]{8,64}$")]
    private static partial Regex Valid();

    public async Task InvokeAsync(HttpContext context)
    {
        var incoming = context.Request.Headers[HeaderName].ToString();
        var correlationId = Valid().IsMatch(incoming) ? incoming : Guid.NewGuid().ToString("N");
        context.Items[ItemKey] = correlationId;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });
        using (logger.BeginScope(new Dictionary<string, object> { [ItemKey] = correlationId }))
        {
            await next(context);
        }
    }
}

public sealed class HttpCorrelationContext(IHttpContextAccessor accessor) : ICorrelationContext
{
    public string CorrelationId =>
        accessor.HttpContext?.Items[CorrelationMiddleware.ItemKey] as string ?? Guid.NewGuid().ToString("N");
}
