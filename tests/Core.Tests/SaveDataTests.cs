using System;
using ZairaPet.Core;
using Xunit;

namespace ZairaPet.Tests;

public class SaveDataTests
{
    [Fact]
    public void Roundtrip_keeps_values_and_nan_positions()
    {
        var s = SaveData.Capture(new Needs { Hunger = 0.4 }, 0.5, 700, double.NaN, 300);
        var back = SaveData.FromJson(s.ToJson());
        Assert.Equal(0.4, back.Hunger);
        Assert.Equal(700, back.BowlX);
        Assert.True(double.IsNaN(back.PerchX));
    }

    [Fact]
    public void Time_away_makes_the_cat_hungry_but_rested()
    {
        var s = new SaveData { Hunger = 0.1, Energy = 0.2, SavedUtc = new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc) };
        var n = s.RestoreNeeds(new DateTime(2026, 1, 1, 20, 0, 0, DateTimeKind.Utc));
        Assert.Equal(1, n.Hunger);   // 8 h cap, 1/7200 per s
        Assert.Equal(1, n.Energy);
    }

    [Fact]
    public void Corrupt_file_gives_defaults()
    {
        Assert.Equal(1, SaveData.FromJson("{ not json").BowlFood);
    }
}
