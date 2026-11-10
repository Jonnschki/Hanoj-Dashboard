using LifeDashboard.Core.Domain;
using LifeDashboard.Core.Sync;
using LifeDashboard.Core.Tests.TestDoubles;

namespace LifeDashboard.Core.Tests.Sync;

public sealed class UserSettingsRulesTests
{
    [Fact]
    public async Task Request_rules_from_user_settings_override_engine_defaults()
    {
        var bed = new SyncTestBed();
        bed.Google.Seed(bed.PrivateA, SyncTestBed.NewEvent("Laufen", description: "Prater"));
        var settings = new UserSettings { PrivateWorkDetailLevel = MirrorDetailLevel.Full };

        await bed.Engine.RunAsync(bed.Request() with { Rules = settings.ToRules() });

        bed.Microsoft.EventsOf(bed.Work).Should().ContainSingle().Which.Title.Should().Be("Laufen");
    }

    [Fact]
    public async Task Request_rules_can_disable_private_to_work_mirroring_entirely()
    {
        var bed = new SyncTestBed();
        bed.Google.Seed(bed.PrivateA, SyncTestBed.NewEvent("Privat"));
        var settings = new UserSettings { PrivateWorkDetailLevel = MirrorDetailLevel.None };

        await bed.Engine.RunAsync(bed.Request() with { Rules = settings.ToRules() });

        bed.Microsoft.EventsOf(bed.Work).Should().BeEmpty();
        bed.Google.EventsOf(bed.PrivateB).Should().ContainSingle("privat → privat bleibt voll");
    }

    [Fact]
    public void Default_settings_produce_busy_only_between_private_and_work()
    {
        var rules = new UserSettings().ToRules();

        rules.Rules.Should().Contain(new MirrorRule(CalendarCategory.Private, CalendarCategory.Work, MirrorDetailLevel.BusyOnly));
        rules.Rules.Should().Contain(new MirrorRule(CalendarCategory.Work, CalendarCategory.Private, MirrorDetailLevel.BusyOnly));
    }

    [Theory]
    [InlineData("spy, qqq;AAPL  msft,spy", new[] { "SPY", "QQQ", "AAPL", "MSFT" })]
    [InlineData("", new string[0])]
    [InlineData(null, new string[0])]
    public void Watchlist_input_is_normalised(string? input, string[] expected)
        => UserSettings.ParseWatchlist(input).Should().Equal(expected);

    [Fact]
    public void Empty_watchlist_falls_back_to_configured_one()
    {
        new UserSettings().EffectiveWatchlist(["SPY"]).Should().Equal("SPY");
        new UserSettings { Watchlist = ["AAPL"] }.EffectiveWatchlist(["SPY"]).Should().Equal("AAPL");
    }
}
