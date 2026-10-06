using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace ZairaPet.Web;

/// <summary>What the page calls (main.js, through dotnet.js): one <see cref="Sim"/>, ticked every frame; the state
/// comes back as numbers (<see cref="State"/>) plus the action and the emote as strings.</summary>
[SupportedOSPlatform("browser")]
public static partial class Api
{
    static Sim? _sim;
    static Sim S => _sim ?? throw new System.InvalidOperationException("Init first");

    public static void Main() { }

    [JSExport] public static void Init(string species, double width, double height, double lengthPx, string saveJson) =>
        _sim = new Sim(species, width, height, lengthPx, saveJson);

    [JSExport] public static void SetPetSize(double w, double h) { S.PetW = w; S.PetH = h; }
    [JSExport] public static void Resize(double width, double height) => S.Resize(width, height);
    [JSExport] public static void Tick(double dt) => S.Tick(dt);

    /// <summary>
    /// 0-1 pet position, 2-3 its velocity, 4 body mode (0 grounded, 1 airborne, 2 held, 3 climbing), 5 facing,
    /// 6-8 bowl x, y, food, 9-10 perch x, y (NaN without one), 11-12 treat x, y (NaN without one),
    /// 13 panting 0..1, 14 tail mood, 15-18 shelf left, top, right, bottom, 19 happy (seconds left),
    /// 20 body pitch in the air (deg, Flight.PitchDeg), 21 how side-on it turns in the air (0..1).
    /// </summary>
    [JSExport] public static double[] State()
    {
        var s = S; var b = s.Body;
        return new[]
        {
            b.Pos.X, b.Pos.Y, b.Vel.X, b.Vel.Y, (double)(int)b.Mode, s.Brain.Facing,
            s.Bowl.Pos.X, s.Bowl.Pos.Y, s.BowlFood,
            s.HasPerch ? s.Perch.Pos.X : double.NaN, s.HasPerch ? s.Perch.Pos.Y : double.NaN,
            s.Treat?.Pos.X ?? double.NaN, s.Treat?.Pos.Y ?? double.NaN,
            s.Exertion.Pant, (double)(int)s.TailMood,
            s.Shelf.Left, s.Shelf.Top, s.Shelf.Right, s.Shelf.Bottom, s.Brain.Happy,
            b.Mode == ZairaPet.Core.BodyMode.Airborne ? ZairaPet.Core.Flight.PitchDeg(b.Vel) : 0,
            b.Mode == ZairaPet.Core.BodyMode.Airborne ? ZairaPet.Core.Flight.SideOn(b.Vel) : 0,
        };
    }

    [JSExport] public static string Action() => S.Brain.Action;
    [JSExport] public static string Emote() => S.Brain.Emote ?? "";
    [JSExport] public static string StateName() => S.Brain.State.ToString();
    [JSExport] public static string Needs() =>
        $"{S.Needs.Hunger:0.00} {S.Needs.Energy:0.00} {S.Needs.Playfulness:0.00} {S.Needs.Affection:0.00}";

    [JSExport] public static int Press(double x, double y) => S.Press(x, y);
    [JSExport] public static void Move(double x, double y, double dt) => S.Move(x, y, dt);
    [JSExport] public static void Release() => S.Release();
    [JSExport] public static bool Stroke(double x, double y, double length) => S.Stroke(x, y, length);
    [JSExport] public static void FillBowl() => S.FillBowl();
    [JSExport] public static void ThrowTreat(double x) => S.ThrowTreat(x);
    [JSExport] public static string Save() => S.Save();
}
