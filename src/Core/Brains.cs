using System;

namespace ZairaPet.Core;

/// <summary>Which brain a species has (profile.json "species").</summary>
public static class Brains
{
    public static PetBrain For(string? species, Random rng) => species switch
    {
        _ => new CatBrain(rng),
    };
}
