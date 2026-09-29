using System;
using ZairaPet.Core;
using Xunit;

namespace ZairaPet.Tests;

public class BrainsTests
{
    [Theory]
    [InlineData("cat")]
    [InlineData("")]
    [InlineData(null)]
    public void A_cat_or_an_unknown_species_gets_the_cat_brain(string? species) =>
        Assert.IsType<CatBrain>(Brains.For(species, new Random(1)));
}
