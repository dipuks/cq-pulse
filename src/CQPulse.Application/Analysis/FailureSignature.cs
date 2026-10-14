using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using CQPulse.Domain;

namespace CQPulse.Application.Analysis;

/// <summary>
/// A stable grouping key for failures: the failure category plus the error message with volatile parts (GUIDs, numbers,
/// quoted values, hex ids) replaced by placeholders. Computed by CQ Pulse from its own persisted results.
/// </summary>
public static partial class FailureSignature
{
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
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..12];
    }
}
