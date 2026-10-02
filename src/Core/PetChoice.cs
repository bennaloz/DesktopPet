using System;
using System.Collections.Generic;
using System.Linq;

namespace ZairaPet.Core;

/// <summary>
/// Which pet shows up. A build carries the pets it was made with (cats/&lt;name&gt;/, the placeholder fox only in the
/// repo); the tray menu offers them and remembers the one picked (pet.txt in the user folder) for the next start.
/// </summary>
public static class PetChoice
{
    /// <summary>The placeholder from before the real pets: never offered, used only when nothing else is there.</summary>
    public const string Placeholder = "fox";

    /// <summary>
    /// The pet asked for on the command line, else the one last picked from the menu, else the build's own (an export
    /// can name one), else Zaira, else the first there is.
    /// </summary>
    public static string Pick(IReadOnlyCollection<string> available, string? arg, string? lastPicked, string? buildDefault)
    {
        foreach (var name in new[] { arg, lastPicked, buildDefault, "zaira" })
            if (!string.IsNullOrWhiteSpace(name) && available.Contains(name.Trim().ToLowerInvariant()))
                return name.Trim().ToLowerInvariant();
        return Offered(available).FirstOrDefault() ?? available.FirstOrDefault()
               ?? throw new InvalidOperationException("nessun animale in cats/");
    }

    /// <summary>The pets the menu offers, in name order.</summary>
    public static IReadOnlyList<string> Offered(IEnumerable<string> available) =>
        available.Where(n => n != Placeholder).OrderBy(n => n, StringComparer.Ordinal).ToList();

    /// <summary>The file in the user folder that remembers the pet picked from the menu.</summary>
    public static string FileIn(string userDir) => System.IO.Path.Combine(userDir, "pet.txt");

    public static string? Read(string userDir)
    {
        try { return System.IO.File.Exists(FileIn(userDir)) ? System.IO.File.ReadAllText(FileIn(userDir)).Trim() : null; }
        catch (System.IO.IOException) { return null; }
    }

    public static void Write(string userDir, string pet) => System.IO.File.WriteAllText(FileIn(userDir), pet);
}
