using CQPulse.Application.Analysis;
using CQPulse.Domain;

namespace CQPulse.UnitTests;

/// <summary>
/// The failure signature is stored on every failed result, so its rules are pinned here: volatile values must not split
/// a signature, real differences must, and the hash of a known input must never change silently.
/// </summary>
public class FailureSignatureTests
{
    [Theory]
    [InlineData("Order 12345 failed for customer 3f2504e0-4f89-11d3-9a0c-0305e82c3301", "Order 9 failed for customer 6F9619FF-8B86-D011-B42D-00C04FC964FF")]
    [InlineData("Timeout at 2026-10-15T12:34:56.789Z after 3000 ms", "Timeout at 2025-01-01T00:00:00Z after 15 ms")]
    [InlineData("Lock wait 2026-10-15 12:34:56,789 on row 17", "Lock wait 2024-02-29 01:02:03,004 on row 99812")]
    [InlineData(@"Could not load C:\agents\12\work\src\Checkout.cs", @"Could not load C:\agents\7\work\src\Checkout.cs")]
    [InlineData("Missing /tmp/build-4711/out/app.dll", "Missing /tmp/build-99/out/app.dll")]
    [InlineData("Access violation at 0x7FFE1234", "Access violation at 0x00ab")]
    [InlineData("Build a94a8fe5ccb19ba61c4c0873d391e987982fbbd3 failed", "Build 0123456789abcdef0123456789abcdef01234567 failed")]
    [InlineData("User 'alice' not found", "User 'bob' not found")]
    [InlineData("Header \"X-Trace\" rejected", "Header \"X-Other\" rejected")]
    [InlineData("Request took 1.5s on port 8080", "Request took 12.25s on port 443")]
    [InlineData("Element   #submit\tnot found", "Element #submit not found")]
    public void Volatile_values_do_not_split_a_signature(string first, string second)
    {
        Assert.Equal(FailureSignature.Normalize(first), FailureSignature.Normalize(second));
        Assert.Equal(FailureSignature.Compute(FailureCategory.ApiError, first), FailureSignature.Compute(FailureCategory.ApiError, second));
    }

    [Fact]
    public void Stack_traces_group_across_line_numbers_and_line_endings()
    {
        const string first = "NullReferenceException: Object reference not set\n   at Shop.Checkout.Pay() in /src/Checkout.cs:line 42\n   at Shop.Api.Post() in /src/Api.cs:line 17";
        const string second = "NullReferenceException: Object reference not set\r\n   at Shop.Checkout.Pay() in /src/Checkout.cs:line 43\r\n   at Shop.Api.Post() in /src/Api.cs:line 18";
        const string otherFrame = "NullReferenceException: Object reference not set\n   at Shop.Checkout.Refund() in /src/Checkout.cs:line 42\n   at Shop.Api.Post() in /src/Api.cs:line 17";

        Assert.Equal(FailureSignature.Compute(null, first), FailureSignature.Compute(null, second));
        Assert.NotEqual(FailureSignature.Compute(null, first), FailureSignature.Compute(null, otherFrame));
        Assert.Equal("NullReferenceException: Object reference not set at Shop.Checkout.Pay() in /src/Checkout.cs:line <n> at Shop.Api.Post() in /src/Api.cs:line <n>",
            FailureSignature.Normalize(first));
    }

    [Theory]
    [InlineData("Element #submit not found", "Element #cancel not found")]
    [InlineData(@"Could not load C:\src\Checkout.cs", @"Could not load C:\src\Payment.cs")]
    [InlineData("Connection refused", "Connection reset")]
    public void Real_differences_stay_separate(string first, string second) =>
        Assert.NotEqual(FailureSignature.Compute(FailureCategory.ApiError, first), FailureSignature.Compute(FailureCategory.ApiError, second));

    [Fact]
    public void The_category_is_part_of_the_signature() =>
        Assert.NotEqual(FailureSignature.Compute(FailureCategory.Timeout, "Request failed"), FailureSignature.Compute(FailureCategory.ApiError, "Request failed"));

    [Fact]
    public void Hex_ids_shorter_than_16_characters_without_0x_are_a_documented_limitation()
    {
        // Digits inside them become <n> but the letters stay, so two such ids normalize differently.
        Assert.Equal("Commit a<n>b<n>c<n>d<n>e<n>f<n> failed", FailureSignature.Normalize("Commit a1b2c3d4e5f6 failed"));
        Assert.NotEqual(FailureSignature.Normalize("Commit a1b2c3d4e5f6 failed"), FailureSignature.Normalize("Commit 0f9e8d7c6b5a failed"));
    }

    [Fact]
    public void Only_the_first_300_normalized_characters_count()
    {
        var prefix = new string('x', 300);
        Assert.Equal(FailureSignature.Compute(null, prefix + " tail one"), FailureSignature.Compute(null, prefix + " tail two"));
        Assert.Equal(300, FailureSignature.Normalize(prefix + " tail").Length);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \r\n ")]
    public void Missing_messages_share_one_signature(string? message)
    {
        Assert.Equal("(no message)", FailureSignature.Normalize(message));
        Assert.Equal("e29e98de6125", FailureSignature.Compute(null, message));
    }

    /// <summary>Expected values are SHA-256("{category}|{normalized}")[..12], computed independently of this code.</summary>
    [Theory]
    [InlineData(FailureCategory.Timeout, "Timeout after 3000 ms for order 42", "f25d3acbcbdf")]
    [InlineData(FailureCategory.ApiError, "Order 12345 failed for customer 3f2504e0-4f89-11d3-9a0c-0305e82c3301", "f1f04eac2e78")]
    [InlineData(FailureCategory.UiElement, "Element #submit not found", "fed4bcef9ee5")]
    [InlineData(FailureCategory.Timeout, "Simulated Timeout failure.", "062c151ff8c6")]
    public void Signatures_of_known_inputs_never_change(FailureCategory category, string message, string expected)
    {
        var signature = FailureSignature.Compute(category, message);

        Assert.Equal(expected, signature);
        Assert.Equal(FailureSignature.Length, signature.Length);
        Assert.Matches("^[0-9a-f]{12}$", signature);
    }
}
