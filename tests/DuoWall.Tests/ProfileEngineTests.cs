using DuoWall.Core.Profiles;
using DuoWall.Core.Widgets;
using Xunit;

namespace DuoWall.Tests;

public sealed class ProfileEngineTests
{
    private readonly ProfileEvaluationEngine _engine = new();

    [Fact]
    public void Evaluate_ResolvesOvernightTimeSchedulesAndPriorityConflicts()
    {
        var profiles = BuiltInProfilePresets.CreateDefaultProfiles();
        foreach (var p in profiles)
            p.IsEnabled = true;

        // 23:30 should match Night Profile (21:00 -> 06:00)
        var nightRes = _engine.Evaluate(profiles, new DesktopContextSnapshot
        {
            LocalNow = new DateTime(2026, 11, 11, 23, 30, 0)
        });
        Assert.NotNull(nightRes.SelectedProfile);
        Assert.Equal("preset-night", nightRes.SelectedProfile!.ProfileId);

        // Add high-priority FullScreenApp override
        var fsProfile = new DesktopProfile
        {
            ProfileId = "gaming-fs",
            Name = "Fullscreen Pause",
            IsEnabled = true,
            Priority = 100,
            Triggers = { new ProfileTrigger { IsEnabled = true, TriggerType = ProfileTriggerType.FullScreenApp, RequireFullScreenActive = true } }
        };
        profiles.Add(fsProfile);

        var fsRes = _engine.Evaluate(profiles, new DesktopContextSnapshot
        {
            LocalNow = new DateTime(2026, 11, 11, 23, 30, 0),
            IsFullScreenAppRunning = true
        });
        Assert.Equal("gaming-fs", fsRes.SelectedProfile!.ProfileId);
    }

    [Fact]
    public void Evaluate_RespectsManualOverrideAndDisabledProfiles()
    {
        var profiles = BuiltInProfilePresets.CreateDefaultProfiles();
        // All presets default to disabled -> never switches unexpectedly!
        var noSwitch = _engine.Evaluate(profiles, new DesktopContextSnapshot
        {
            LocalNow = new DateTime(2026, 11, 11, 8, 0, 0)
        });
        Assert.Null(noSwitch.SelectedProfile);

        // Manual override selects explicit profile cleanly
        var manual = _engine.Evaluate(profiles, new DesktopContextSnapshot
        {
            ManualOverrideProfileId = "preset-monitoring"
        });
        Assert.True(manual.IsManualOverride);
        Assert.Equal("preset-monitoring", manual.SelectedProfile!.ProfileId);
    }
}

