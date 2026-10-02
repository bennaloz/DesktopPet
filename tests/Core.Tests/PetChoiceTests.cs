using System;
using System.IO;
using ZairaPet.Core;
using Xunit;

namespace ZairaPet.Tests;

/// <summary>Which pet shows up: the command line, then the one picked from the tray menu, then the build's own.</summary>
public class PetChoiceTests
{
    static readonly string[] All = { "bretzel", "fox", "sally", "zaira" };

    [Fact]
    public void The_command_line_wins() =>
        Assert.Equal("sally", PetChoice.Pick(All, "sally", "bretzel", "zaira"));

    [Fact]
    public void Then_the_pet_last_picked_from_the_menu() =>
        Assert.Equal("bretzel", PetChoice.Pick(All, null, "bretzel", "sally"));

    [Fact]
    public void Then_the_one_the_build_was_made_for() =>
        Assert.Equal("bretzel", PetChoice.Pick(All, "", null, "bretzel"));

    [Fact]
    public void Otherwise_Zaira() =>
        Assert.Equal("zaira", PetChoice.Pick(All, null, null, null));

    [Fact]
    public void A_pet_the_build_does_not_have_is_passed_over() =>
        Assert.Equal("sally", PetChoice.Pick(new[] { "sally" }, "zaira", "bretzel", null));

    [Fact]
    public void The_placeholder_fox_only_when_nothing_else_is_there()
    {
        Assert.Equal("bretzel", PetChoice.Pick(new[] { "bretzel", "fox" }, null, null, null));
        Assert.Equal("fox", PetChoice.Pick(new[] { "fox" }, null, null, null));
    }

    [Fact]
    public void The_menu_offers_every_pet_but_the_placeholder() =>
        Assert.Equal(new[] { "bretzel", "sally", "zaira" }, PetChoice.Offered(All));

    [Fact]
    public void The_pick_is_remembered_in_the_user_folder()
    {
        string dir = Path.Combine(Path.GetTempPath(), "petchoice-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            Assert.Null(PetChoice.Read(dir));
            PetChoice.Write(dir, "sally");
            Assert.Equal("sally", PetChoice.Read(dir));
        }
        finally { Directory.Delete(dir, true); }
    }
}
