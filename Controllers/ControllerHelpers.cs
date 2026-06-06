namespace FitQuest.Api.Controllers;

/// <summary>
/// Shared static helpers used across multiple controllers.
/// Centralises logic that was previously duplicated in PlanController,
/// CoachController, WeekPlanController, NutritionController, StatsController
/// and TrainingSessionsController.
/// </summary>
internal static class ControllerHelpers
{
    // ── Muscle-group constants ────────────────────────────────────────────────

    internal static readonly string[] MuscleGroups =
        ["legs", "chest", "back", "shoulders", "arms", "full_body"];

    internal static readonly string[] DefaultMuscleGroupOrder =
        ["full_body", "chest", "back", "shoulders", "arms", "legs"];

    // ── Language resolution ───────────────────────────────────────────────────

    /// <summary>
    /// Resolves the output language ("zh" | "en") from the Accept-Language
    /// header, falling back to the stored profile language preference.
    /// </summary>
    internal static string ResolveOutputLanguage(string? acceptLanguage, string? profileLanguage)
    {
        if (string.IsNullOrWhiteSpace(acceptLanguage))
            return ResolveStoredLanguage(profileLanguage);

        var firstLanguage = acceptLanguage
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault()?
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault()
            ?.ToLowerInvariant();

        if (firstLanguage is null) return "zh";
        return firstLanguage.StartsWith("zh") ? "zh" : "en";
    }

    internal static string ResolveStoredLanguage(string? profileLanguage) =>
        profileLanguage switch
        {
            "zh-CN" => "zh",
            "en-US" => "en",
            _       => "zh",
        };

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
