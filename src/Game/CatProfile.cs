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
    /// <summary>Which brain it has: "cat" (the default) or "rabbit".</summary>
    [JsonPropertyName("species")] public string Species { get; set; } = "cat";
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
    /// <summary>Tail bones posed by the mood while walking or running (root first). Models without them keep the clip's tail.</summary>
    [JsonPropertyName("tail_bones")] public TailBones TailBones { get; set; } = new();
    /// <summary>"cat" (height and curl say the mood) or "dog" (the wag does).</summary>
    [JsonPropertyName("tail_style")] public string TailStyle { get; set; } = "cat";
    [JsonIgnore] public ZairaPet.Core.TailStyle Tail => TailStyle == "dog" ? ZairaPet.Core.TailStyle.Dog : ZairaPet.Core.TailStyle.Cat;
    /// <summary>Actions whose tail follows the mood; empty: the gaits (walk, trot, lope, run).</summary>
    [JsonPropertyName("mood_tail_actions")] public List<string> MoodTailActions { get; set; } = new();
    /// <summary>Floppy ear chains (root first), bounced by <see cref="EarModifier"/>; none for pricked-up ears.</summary>
    [JsonPropertyName("ear_bones")] public List<List<string>> EarBones { get; set; } = new();
    /// <summary>The jaw a dog pants with (<see cref="PantModifier"/>): the clips keep it shut, the game opens it when she is
    /// winded. Empty: no panting.</summary>
    [JsonPropertyName("pant_bone")] public string PantBone { get; set; } = "";
    [JsonPropertyName("material_colors")] public Dictionary<string, float[]> MaterialColors { get; set; } = new();
    [JsonPropertyName("actions")] public Dictionary<string, ActionClip> Actions { get; set; } = new();

    /// <summary>Folder of the profile, filled in by <see cref="Load"/>.</summary>
    [JsonIgnore] public string Folder { get; private set; } = "";

    public static CatProfile Load(string folder)
    {
        // (Godot's file access: res:// is inside the package in an exported build)
        string json = Godot.FileAccess.GetFileAsString($"{folder}/profile.json");
        if (json == "") throw new System.InvalidOperationException($"profilo mancante: {folder}/profile.json");
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
            "aim" => "prejump",
            "fall" => "jump",
            "run" => "walk",
            "lope" => "run",
            "trot" => "walk",
            "stalk" => "sit",
            "wiggle" => "stalk",
            "swat" => "idle",
            "sit" => "idle",
            "loaf" => "crouch",
            "tuck" => "loaf",
            "crouch" => "sit",
            "liedown" => "crouch",
            "climb" => "walk",
            "eat" => "idle",
            // a rabbit's actions, for a model that lacks some of them
            "hop" => "walk",
            "petted" => "purr",
            "flop" => "flopsleep",
            "flopsleep" => "sleep",
            "binky" => "jump",
            "thump" => "idle",
            "groom" => "sit",
            // a dog's
            "bark" => "meow",
            _ => null,
        };
        return fallback == null ? null : Resolve(fallback);
    }
}

public sealed class ActionClip
{
    [JsonPropertyName("anims")] public List<string> Anims { get; set; } = new();
    /// <summary>
    /// Optional mirror images of <see cref="Anims"/> (same order) for when the cat faces right: resting clips curl
    /// the tail round one side, and the other side is the one hidden from the viewer.
    /// </summary>
    [JsonPropertyName("anims_right")] public List<string> AnimsRight { get; set; } = new();
    [JsonPropertyName("loop")] public bool Loop { get; set; } = true;
    [JsonPropertyName("speed")] public double Speed { get; set; } = 1;
    /// <summary>For locomotion: ground speed at which Speed looks right; the clip is scaled to the real speed.</summary>
    [JsonPropertyName("ref_speed_px")] public double RefSpeedPx { get; set; }
    [JsonPropertyName("breathe")] public bool Breathe { get; set; }
    [JsonPropertyName("vibrate")] public bool Vibrate { get; set; }
    /// <summary>
    /// For poses that only read from one side (curled up: the face is inside the curl): the facing (1 or -1)
    /// the cat turns to while the clip plays. 0 keeps the facing the brain chose.
    /// </summary>
    [JsonPropertyName("show_side")] public int ShowSide { get; set; }
    /// <summary>Seconds to blend into this clip from the previous one: slow for lying down, quick otherwise.</summary>
    [JsonPropertyName("blend")] public double Blend { get; set; } = 0.18;
}

public sealed class TailBones
{
    /// <summary>The body bone the tail angles are measured from (points forward along the back).</summary>
    [JsonPropertyName("root")] public string Root { get; set; } = "Hips";
    [JsonPropertyName("bones")] public List<string> Bones { get; set; } = new() { "Tail1", "Tail2", "Tail3", "Tail4", "Tail5" };
}

public sealed class GazeBones
{
    [JsonPropertyName("neck")] public string Neck { get; set; } = "Neck";
    [JsonPropertyName("head")] public string Head { get; set; } = "Head";
    /// <summary>Optional chest that turns a little with the look, and the bones under it that must stay put
    /// (the shoulder blades, so the front legs do not slide).</summary>
    [JsonPropertyName("chest")] public string Chest { get; set; } = "";
    [JsonPropertyName("keep")] public List<string> Keep { get; set; } = new();
    /// <summary>Bones hanging off the head (a lop rabbit's ears): when the look tips the head up or down they keep
    /// hanging where the clip had them, turning with the head only sideways.</summary>
    [JsonPropertyName("hang")] public List<string> Hang { get; set; } = new();
    /// <summary>How far (degrees) the look may tip the head up from where the clip holds it: a rabbit's skin
    /// stretches from the head down over the shoulders, and tears long before a cat's neck would stop.</summary>
    [JsonPropertyName("look_up_deg")] public float LookUpDeg { get; set; } = 180f;
    /// <summary>How much of a look up the neck takes (the rest is the head's): a rabbit's neck is short and its
    /// skin is the chest's; it tips its head up at the skull.</summary>
    [JsonPropertyName("neck_up_share")] public float NeckUpShare { get; set; } = 1f;
    /// <summary>How far (degrees) the look may turn the head round to the side from where the clip points it: turned
    /// right round to face the screen, the rabbit's lop ear (grown onto the cheek above and onto the shoulder below)
    /// fanned out and tore.</summary>
    [JsonPropertyName("turn_deg")] public float TurnDeg { get; set; } = 180f;
}
