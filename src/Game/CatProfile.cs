using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ZairaPet.Game;

/// <summary>
/// cats/&lt;name&gt;/profile.json: how a GLB model plays the logical actions the brain asks for.
/// Swapping the cat means dropping a new folder, not changing code.
/// </summary>
public sealed class CatProfile
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("model")] public string Model { get; set; } = "";
    /// <summary>Nose-to-tail length on screen.</summary>
    [JsonPropertyName("length_px")] public double LengthPx { get; set; } = 150;
    /// <summary>Extra yaw if the model does not face glTF +Z.</summary>
    [JsonPropertyName("yaw_offset_deg")] public double YawOffsetDeg { get; set; }
    /// <summary>How much of its face the cat turns toward the viewer while walking sideways.</summary>
    [JsonPropertyName("three_quarter_deg")] public double ThreeQuarterDeg { get; set; } = 25;
    /// <summary>How far ahead of the body centre the mouth reaches when eating, as a fraction of the length.</summary>
    [JsonPropertyName("eat_reach")] public double EatReach { get; set; } = 0.3;
    /// <summary>Bones turned to look at things (neck optional). Models without them simply do not look around.</summary>
    [JsonPropertyName("gaze_bones")] public GazeBones GazeBones { get; set; } = new();
    [JsonPropertyName("material_colors")] public Dictionary<string, float[]> MaterialColors { get; set; } = new();
    [JsonPropertyName("actions")] public Dictionary<string, ActionClip> Actions { get; set; } = new();

    /// <summary>Folder of the profile, filled in by <see cref="Load"/>.</summary>
    [JsonIgnore] public string Folder { get; private set; } = "";

    public static CatProfile Load(string folder)
    {
        string json = System.IO.File.ReadAllText(System.IO.Path.Combine(folder, "profile.json"));
        var p = JsonSerializer.Deserialize<CatProfile>(json, new JsonSerializerOptions { ReadCommentHandling = JsonCommentHandling.Skip })
                ?? throw new System.InvalidOperationException($"profilo vuoto in {folder}");
        p.Folder = folder;
        return p;
    }

    /// <summary>The clip for an action, falling back to a similar one when the model lacks it.</summary>
    public ActionClip? Resolve(string action)
    {
        if (Actions.TryGetValue(action, out var clip)) return clip;
        string? fallback = action switch
        {
            "sleep" => "sit",
            "purr" => "sit",
            "meow" => "idle",
            "held" => "idle",
            "land" => "idle",
            "prejump" => "idle",
            "fall" => "jump",
            "run" => "walk",
            "lope" => "run",
            "trot" => "walk",
            "stalk" => "sit",
            "wiggle" => "stalk",
            "swat" => "idle",
            "sit" => "idle",
            "climb" => "walk",
            "eat" => "idle",
            _ => null,
        };
        return fallback == null ? null : Resolve(fallback);
    }
}

public sealed class ActionClip
{
    [JsonPropertyName("anims")] public List<string> Anims { get; set; } = new();
    [JsonPropertyName("loop")] public bool Loop { get; set; } = true;
    [JsonPropertyName("speed")] public double Speed { get; set; } = 1;
    /// <summary>For locomotion: ground speed at which Speed looks right; the clip is scaled to the real speed.</summary>
    [JsonPropertyName("ref_speed_px")] public double RefSpeedPx { get; set; }
    [JsonPropertyName("breathe")] public bool Breathe { get; set; }
    [JsonPropertyName("vibrate")] public bool Vibrate { get; set; }
}

public sealed class GazeBones
{
    [JsonPropertyName("neck")] public string Neck { get; set; } = "Neck";
    [JsonPropertyName("head")] public string Head { get; set; } = "Head";
}
