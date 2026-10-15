using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using CQPulse.Domain;

namespace CQPulse.Application.Analysis;

/// <summary>
/// A stable grouping key for failures: the failure category plus the error message with volatile parts replaced by
/// placeholders, in this order: GUIDs → <c>&lt;id&gt;</c>; <c>0x</c> hex and hex runs of 16+ chars → <c>&lt;hex&gt;</c>;
/// single- or double-quoted values → <c>&lt;value&gt;</c>; digit runs (with an optional decimal part) → <c>&lt;n&gt;</c>;
/// whitespace (including newlines) collapsed to one space; the result is cut to 300 chars. So numeric ids, timestamps,
/// durations, ports, line numbers in paths and stack frames do not split a signature, while words, file names and type
/// names still do. The signature is the first 12 hex chars of SHA-256 over <c>"{category}|{normalized}"</c>.
/// It is stored on each failed <c>TestCaseResult</c>, so changing these rules requires clearing the stored column and
/// re-running the backfill (see docs/operations.md); otherwise old and new rows would not group together.
/// </summary>
public static partial class FailureSignature
{
    /// <summary>Length of a signature (lowercase hex).</summary>
    public const int Length = 12;

    [GeneratedRegex(@"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b")]
    private static partial Regex Guids();

    [GeneratedRegex(@"\b0x[0-9a-fA-F]+\b|\b[0-9a-fA-F]{16,}\b")]
    private static partial Regex HexIds();

    [GeneratedRegex(@"(""[^""]*""|'[^']*')")]
    private static partial Regex Quoted();

    [GeneratedRegex(@"\d+(\.\d+)?")]
    private static partial Regex Numbers();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    public static string Normalize(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return "(no message)";
        var text = Guids().Replace(message, "<id>");
        text = HexIds().Replace(text, "<hex>");
        text = Quoted().Replace(text, "<value>");
        text = Numbers().Replace(text, "<n>");
        text = Whitespace().Replace(text, " ").Trim();
        return text.Length <= 300 ? text : text[..300];
    }

    /// <summary>Short, deterministic id (first 12 hex chars of SHA-256 over category and normalized message).</summary>
    public static string Compute(FailureCategory? category, string? message)
    {
        var key = $"{category?.ToString() ?? "Uncategorized"}|{Normalize(message)}";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..Length];
    }
}
