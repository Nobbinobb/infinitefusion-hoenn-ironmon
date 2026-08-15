using System.Text.Json;

namespace Ironmon.Tracker.Tests.Connection;

/// <summary>
/// Verifies diagnostic reports serialize only independently authorized tracker-owned groups.
/// </summary>
public sealed class TrackerDiagnosticReportBuilderTests
{
    private static readonly DateTimeOffset GeneratedAt = new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Initializes the diagnostic report builder tests.
    /// </summary>
    public TrackerDiagnosticReportBuilderTests()
    {
    }

    /// <summary>
    /// Verifies each local capability includes only its corresponding report fields.
    /// </summary>
    /// <param name="capability">The independently granted local capability.</param>
    /// <param name="expectedFields">The report fields enabled by the capability.</param>
    [Theory]
    [InlineData(DiagnosticCapabilities.TrackerRawState, "connection,run_state")]
    [InlineData(DiagnosticCapabilities.TrackerPersistedKnowledge, "knowledge")]
    [InlineData(DiagnosticCapabilities.TrackerProtocolHistory, "last_protocol_error,protocol_history")]
    public void BuildIncludesOnlyAuthorizedGroups(string capability, string expectedFields)
    {
        DiagnosticAccessGrant grant = new("secret-key-id", "secret-token-id", GeneratedAt, null, "secret note", [capability], [capability]);
        DiagnosticAccessSnapshot access = DiagnosticAccessSnapshot.Active(grant, []);
        TrackerDiagnosticEntry entry = new(GeneratedAt, TrackerDiagnosticDirection.Incoming, "response", "secret-history");

        string report = TrackerDiagnosticReportBuilder.Build(access, new { raw_connection = "secret-connection" }, new { raw_run = "secret-run" }, new { persisted = "secret-knowledge" }, "secret-error", [entry], GeneratedAt);
        using JsonDocument document = JsonDocument.Parse(report);
        HashSet<string> fields = [.. document.RootElement.EnumerateObject().Select(property => property.Name)];

        Assert.Contains("generated_at", fields);
        foreach (string expected in expectedFields.Split(','))
            Assert.Contains(expected, fields);

        string[] protectedFields = ["connection", "run_state", "knowledge", "last_protocol_error", "protocol_history"];
        foreach (string hidden in protectedFields.Except(expectedFields.Split(','), StringComparer.Ordinal))
            Assert.DoesNotContain(hidden, fields);

        Assert.DoesNotContain("secret-token-id", report, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-key-id", report, StringComparison.Ordinal);
        Assert.DoesNotContain("secret note", report, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies no-access reports contain no protected diagnostic group.
    /// </summary>
    [Fact]
    public void BuildWithoutAccessContainsOnlyGenerationTime()
    {
        string report = TrackerDiagnosticReportBuilder.Build(DiagnosticAccessSnapshot.None(), new { secret = true }, new { secret = true }, new { secret = true }, "secret", [], GeneratedAt);
        using JsonDocument document = JsonDocument.Parse(report);

        Assert.Equal("generated_at", Assert.Single(document.RootElement.EnumerateObject()).Name);
    }
}
