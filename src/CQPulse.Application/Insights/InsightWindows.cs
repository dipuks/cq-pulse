namespace CQPulse.Application.Insights;

public static class InsightWindows
{
    /// <summary>
    /// The last <paramref name="days"/> whole UTC days including today, as the half-open interval [from, to), plus the
    /// start of the equally long window immediately before it.
    /// </summary>
    public static InsightWindow LastDays(int days, DateTimeOffset now)
    {
        days = Math.Clamp(days, 1, 365);
        var to = new DateTimeOffset(now.UtcDateTime.Date.AddDays(1), TimeSpan.Zero);
        var from = to.AddDays(-days);
        return new InsightWindow(from, to, from.AddDays(-days));
    }
}
