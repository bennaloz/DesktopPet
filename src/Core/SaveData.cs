using System;
using System.Text.Json;

namespace ZairaPet.Core;

/// <summary>What survives a restart. Stored as JSON in the Godot user folder.</summary>
public sealed class SaveData
{
    public double Hunger { get; set; } = 0.3;
    public double Energy { get; set; } = 0.8;
    public double Playfulness { get; set; } = 0.3;
    public double Affection { get; set; } = 0.5;
    public double BowlFood { get; set; } = 1;
    public double BowlX { get; set; } = double.NaN;
    public double PerchX { get; set; } = double.NaN;
    public double CatX { get; set; } = double.NaN;
    public DateTime SavedUtc { get; set; } = DateTime.UtcNow;

    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    public static SaveData FromJson(string json)
    {
        try { return JsonSerializer.Deserialize<SaveData>(json, Options) ?? new SaveData(); }
        catch (JsonException) { return new SaveData(); }
    }

    public static SaveData Capture(Needs n, double bowlFood, double bowlX, double perchX, double catX) => new()
    {
        Hunger = n.Hunger, Energy = n.Energy, Playfulness = n.Playfulness, Affection = n.Affection,
        BowlFood = bowlFood, BowlX = bowlX, PerchX = perchX, CatX = catX, SavedUtc = DateTime.UtcNow,
    };

    /// <summary>
    /// Where a pet's save is read from: its own file (save-&lt;pet&gt;.json), or for Zaira, until she has written hers,
    /// the save.json of when she was the only pet.
    /// </summary>
    public static string ReadPathFor(string dir, string pet)
    {
        string own = WritePathFor(dir, pet);
        string old = System.IO.Path.Combine(dir, "save.json");
        return pet == "zaira" && !System.IO.File.Exists(own) && System.IO.File.Exists(old) ? old : own;
    }

    /// <summary>Where a pet's save is written: every pet has its own.</summary>
    public static string WritePathFor(string dir, string pet) => System.IO.Path.Combine(dir, $"save-{pet}.json");

    /// <summary>Needs as they are now, counting the time the program was closed (at most 8 hours, awake).</summary>
    public Needs RestoreNeeds(DateTime nowUtc)
    {
        var n = new Needs { Hunger = Hunger, Energy = Energy, Playfulness = Playfulness, Affection = Affection };
        double away = Math.Clamp((nowUtc - SavedUtc).TotalSeconds, 0, 8 * 3600);
        n.Tick(away, sleeping: true);   // time away counts as rest: coming back to an exhausted cat is no fun
        return n;
    }
}
