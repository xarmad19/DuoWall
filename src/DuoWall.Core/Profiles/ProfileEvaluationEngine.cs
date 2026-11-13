using DuoWall.Core.Widgets;

namespace DuoWall.Core.Profiles;

public sealed class DesktopContextSnapshot
{
    public DateTime LocalNow { get; init; } = DateTime.Now;
    public bool IsStartup { get; init; }
    public string TopologySignature { get; init; } = string.Empty;
    public bool IsFullScreenAppRunning { get; init; }
    public bool IsOnBattery { get; init; }
    public int BatteryPercent { get; init; } = 100;
    public string? ManualOverrideProfileId { get; init; }
}

public sealed class ProfileEvaluationResult
{
    public DesktopProfile? SelectedProfile { get; init; }
    public string MatchReason { get; init; } = "No matching automatic profile";
    public bool IsManualOverride { get; init; }
}

/// <summary>
/// Pure deterministic rule evaluator for Intelligent Desktop Profile switching.
/// Respects explicit enable flags, overnight time ranges, priority conflict resolution,
/// and manual overrides so profiles never switch unexpectedly.
/// </summary>
public sealed class ProfileEvaluationEngine
{
    public ProfileEvaluationResult Evaluate(
        IReadOnlyList<DesktopProfile> profiles,
        DesktopContextSnapshot context)
    {
        if (profiles == null || profiles.Count == 0)
            return new ProfileEvaluationResult { MatchReason = "No profiles configured" };

        if (!string.IsNullOrWhiteSpace(context.ManualOverrideProfileId))
        {
            var manual = profiles.FirstOrDefault(p =>
                string.Equals(p.ProfileId, context.ManualOverrideProfileId, StringComparison.OrdinalIgnoreCase));
            if (manual != null)
            {
                return new ProfileEvaluationResult
                {
                    SelectedProfile = manual,
                    MatchReason = $"Manual lock: {manual.Name}",
                    IsManualOverride = true
                };
            }
        }

        var candidates = new List<(DesktopProfile Profile, string Reason)>();

        foreach (var profile in profiles.Where(p => p.IsEnabled))
        {
            foreach (var trigger in profile.Triggers.Where(t => t.IsEnabled))
            {
                if (DoesTriggerMatch(trigger, context, out string reason))
                {
                    candidates.Add((profile, reason));
                    break;
                }
            }
        }

        if (candidates.Count == 0)
            return new ProfileEvaluationResult { MatchReason = "No enabled automatic triggers matched current context" };

        var winner = candidates
            .OrderByDescending(c => c.Profile.Priority)
            .ThenBy(c => c.Profile.Name, StringComparer.OrdinalIgnoreCase)
            .First();

        return new ProfileEvaluationResult
        {
            SelectedProfile = winner.Profile,
            MatchReason = winner.Reason,
            IsManualOverride = false
        };
    }

    public static bool DoesTriggerMatch(
        ProfileTrigger trigger,
        DesktopContextSnapshot context,
        out string reason)
    {
        reason = string.Empty;
        switch (trigger.TriggerType)
        {
            case ProfileTriggerType.Manual:
                return false;

            case ProfileTriggerType.Startup:
                if (context.IsStartup)
                {
                    reason = "Windows startup trigger";
                    return true;
                }
                return false;

            case ProfileTriggerType.TimeOfDay:
                if (trigger.DaysOfWeek.Count > 0 && !trigger.DaysOfWeek.Contains(context.LocalNow.DayOfWeek))
                    return false;
                if (IsWithinTimeRange(context.LocalNow.TimeOfDay, trigger.StartTime, trigger.EndTime))
                {
                    reason = $"Schedule {trigger.StartTime:hh\\:mm}–{trigger.EndTime:hh\\:mm}";
                    return true;
                }
                return false;

            case ProfileTriggerType.DayOfWeek:
                if (trigger.DaysOfWeek.Count > 0 && trigger.DaysOfWeek.Contains(context.LocalNow.DayOfWeek))
                {
                    reason = $"Day of week ({context.LocalNow.DayOfWeek})";
                    return true;
                }
                return false;

            case ProfileTriggerType.MonitorTopology:
                if (!string.IsNullOrWhiteSpace(trigger.MonitorTopologyPattern) &&
                    context.TopologySignature.Contains(trigger.MonitorTopologyPattern, StringComparison.OrdinalIgnoreCase))
                {
                    reason = $"Monitor layout matched '{trigger.MonitorTopologyPattern}'";
                    return true;
                }
                return false;

            case ProfileTriggerType.FullScreenApp:
                if (context.IsFullScreenAppRunning == trigger.RequireFullScreenActive)
                {
                    reason = context.IsFullScreenAppRunning ? "Full-screen app detected" : "Desktop clear";
                    return true;
                }
                return false;

            case ProfileTriggerType.PowerState:
                bool matchesPower = trigger.PowerCondition switch
                {
                    TriggerPowerCondition.OnAcPower => !context.IsOnBattery,
                    TriggerPowerCondition.OnBattery => context.IsOnBattery,
                    TriggerPowerCondition.BatteryLow => context.IsOnBattery && context.BatteryPercent <= 25,
                    _ => true
                };
                if (matchesPower)
                {
                    reason = $"Power state ({trigger.PowerCondition})";
                    return true;
                }
                return false;

            default:
                return false;
        }
    }

    public static bool IsWithinTimeRange(TimeSpan current, TimeSpan start, TimeSpan end)
    {
        if (start == end) return true;
        if (start < end)
            return current >= start && current < end;

        // Overnight range (e.g. 21:00 to 06:00)
        return current >= start || current < end;
    }
}

