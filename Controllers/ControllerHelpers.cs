namespace FitQuest.Api.Controllers;

/// <summary>
/// Shared static helpers used across multiple controllers.
/// </summary>
internal static class ControllerHelpers
{
    // ── Muscle-group constants ────────────────────────────────────────────────

    internal static readonly string[] MuscleGroups =
        ["legs", "chest", "back", "shoulders", "arms", "full_body"];

    internal static readonly string[] DefaultMuscleGroupOrder =
        ["full_body", "chest", "back", "shoulders", "arms", "legs"];

    // ── Date helpers ──────────────────────────────────────────────────────────

    /// <summary>Returns the Monday of the week that contains <paramref name="date"/>.</summary>
    internal static DateOnly StartOfWeek(DateOnly date)
    {
        var diff = ((int)date.DayOfWeek + 6) % 7; // Monday = 0 offset
        return date.AddDays(-diff);
    }

    // ── Recovery scoring ──────────────────────────────────────────────────────

    internal static string DescribeRecoveryStatus(int score) => score switch
    {
        >= 80 => "excellent",
        >= 60 => "good",
        >= 40 => "moderate",
        >= 20 => "low",
        _     => "poor",
    };
}
