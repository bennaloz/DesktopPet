# Zaira Desktop Pet — MVP Implementation Plan

> **For agentic workers:** eseguito inline in questa sessione (utente assente di notte). Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** eseguibile (via `run.bat`) con un gatto segnaposto che vive sul desktop secondo la spec.

**Architecture:** logica pura in `src/Core` (nessuna dipendenza Godot, testata con xUnit);
integrazione Win32 in `src/Platform`; nodi Godot in `src/Game`. Coordinate logiche = pixel fisici
dello schermo, y verso il basso.

**Tech Stack:** Godot 4.7.2 .NET (C#, net8.0), xUnit, P/Invoke user32/dwmapi.

**Spec:** `docs/superpowers/specs/2026-09-24-zaira-desktop-pet-design.md`

## Global Constraints

- Godot `C:\develop\personal\tools\Godot_v4.7.2-stable_mono_win64\`, renderer `gl_compatibility`.
- `src/Core/**` non usa `Godot.*`.
- 30 fps massimo; WindowTracker a 8 Hz.
- Nessun segreto, nessuna rete a runtime.
- Modello caricato solo tramite `cats/<nome>/profile.json`.

---

### Task 1: Scaffolding progetto + test

**Files:** `project.godot`, `ZairaDesktopPet.csproj`, `ZairaDesktopPet.sln`, `tests/Core.Tests/Core.Tests.csproj`, `.gitignore`, `run.bat`

- [ ] csproj Godot (`Godot.NET.Sdk/4.7.2`, net8.0) con `<Compile Remove="tests/**" />`.
- [ ] progetto xUnit che compila `../../src/Core/**/*.cs` come link.
- [ ] `project.godot`: finestra borderless, transparent, always_on_top, no_focus, per-pixel transparency, renderer compatibility, max_fps 30, main scene `res://src/Game/Main.tscn`.
- [ ] Verifica: `dotnet build` ok, `dotnet test` ok (0 test), Godot `--headless --import` ok.
- [ ] Commit.

### Task 2: Geometria + SurfaceMap

**Interfaces (produces):**
```csharp
public readonly record struct RectI(int Left, int Top, int Right, int Bottom); // Width, Height, Contains
public readonly record struct WindowInfo(long Handle, RectI Bounds);           // ordinate top→bottom
public enum SurfaceKind { Floor, WindowTop, Perch, Wall }
public sealed record Platform(int Id, SurfaceKind Kind, int Y, int X0, int X1, long Owner);
public sealed class SurfaceMap {
  public static SurfaceMap Build(IReadOnlyList<WindowInfo> windowsTopToBottom,
                                 IReadOnlyList<RectI> workAreas, IReadOnlyList<Platform> extra, int minWidth = 40);
  public IReadOnlyList<Platform> Platforms { get; }
  public Platform? SupportAt(double x, double y, double tolerance);   // piattaforma sotto i piedi
  public Platform? FirstBelow(double x, double y);                    // per la caduta
  public Platform? FindByOwnerNear(long owner, double x, double y, double maxDy);
}
```
- [ ] Test: finestra singola → bordo superiore piattaforma; finestra coperta parzialmente → segmento spezzato;
  finestra completamente coperta → nessuna piattaforma; area di lavoro → Floor; segmenti < minWidth scartati;
  SupportAt/FirstBelow.
- [ ] Implementazione, test verdi, commit.

### Task 3: Needs

```csharp
public sealed class Needs { double Hunger, Energy, Fun, Affection; void Tick(double dt, bool sleeping);
  void Eat(double amount); void Pet(double dt); void Play(double amount); }
```
- [ ] Test: fame sale nel tempo, energia scende sveglia e sale dormendo, valori clamp 0..1.
- [ ] Implementazione, commit.

### Task 4: CatBody (fisica)

```csharp
public enum BodyMode { Grounded, Airborne, Held }
public sealed class CatBody { Vec2 Pos; Vec2 Vel; BodyMode Mode; Platform? Support;
  void Step(double dt, SurfaceMap map, double walkVelX);   // appoggio, caduta, atterraggio, bordi
  void JumpTo(Vec2 target, double apexAbove);               // traiettoria balistica
  void Grab(); void Release(Vec2 throwVel); void MoveHeld(Vec2 pos);
  void FollowOwner(SurfaceMap map); event Action<Platform>? Landed; }
```
- [ ] Test: cade e atterra sul pavimento; cammina fuori dal bordo → cade; salto raggiunge il bersaglio;
  finestra sparita → cade; finestra spostata → segue.
- [ ] Implementazione, commit.

### Task 5: Navigator

```csharp
public sealed record NavStep(NavStepKind Kind, double X, Platform Target); // Walk, Jump, Drop
public static class Navigator { List<NavStep>? FindPath(SurfaceMap map, Platform from, double fromX,
  Platform to, double toX, double maxJumpUp, double maxJumpGap); }
```
- [ ] Test: stessa piattaforma → Walk; piattaforma sopra raggiungibile → Walk+Jump; troppo alta → null;
  discesa → Drop; due salti in catena.
- [ ] Implementazione, commit.

### Task 6: CatBrain

```csharp
public enum CatState { Idle, Wander, Travel, Zoomies, Eat, Sleep, Sit, Meow, ChaseTreat, Petted, Held, Falling, Landing }
public interface IWorld { Platform? BowlPlatform; double BowlX; double BowlFood; Platform PerchTop; double PerchX;
  Vec2? TreatPos; void EatFromBowl(double amount); void ConsumeTreat(); }
public sealed class CatBrain { CatState State; string Action; int Facing;
  void Update(double dt, CatBody body, Needs needs, SurfaceMap map, IWorld world, Random rng);
  void OnGrab(); void OnRelease(); void OnPetting(double dt); void Summon(Vec2 pos); }
```
- [ ] Test: fame alta + ciotola piena → arriva e mangia; energia bassa → va al trespolo e dorme;
  fame alta + ciotola vuota → Meow; bocconcino → ChaseTreat e lo consuma.
- [ ] Implementazione, commit.

### Task 7: Win32 + WindowTracker

- [ ] P/Invoke: EnumWindows, IsWindowVisible, IsIconic, GetWindowLongPtr, DwmGetWindowAttribute
  (EXTENDED_FRAME_BOUNDS=9, CLOAKED=14), GetClassName, EnumDisplayMonitors+GetMonitorInfo (rcWork),
  SetWindowLongPtr (WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE per l'overlay).
- [ ] Esclusioni: nostra finestra, cloaked, minimizzate, tool window, Progman/WorkerW/Shell_TrayWnd, dimensione < 80 px.
- [ ] Verifica manuale: dump della lista finestre a log.

### Task 8: Overlay + CatNode + visual GLB + profile

- [ ] Main.tscn: Camera3D ortografica, luce, WorldEnvironment trasparente.
- [ ] Overlay: posizione/dimensione sul desktop virtuale, conversione px↔mondo, passthrough dinamico.
- [ ] CatVisual: carica GLB dal profilo, scala alla lunghezza in px, tinta, AnimationPlayer con mappatura azioni,
  orientamento, posa "held" e respiro nel sonno.
- [ ] Verifica: `--selftest` salva screenshot del viewport con il gatto visibile.

### Task 9: Props, input, tray, salvataggio

- [ ] Ciotola (trascinabile, doppio clic = riempi), trespolo (piattaforma extra), bocconcino (cade dal cursore).
- [ ] Input: presa/trascinamento/rotazione, carezza, lancio.
- [ ] StatusIndicator con menu; SaveData JSON in `user://save.json` ogni 30 s e all'uscita.
- [ ] Verifica: selftest + screenshot desktop; README.

### Task 10 (se avanza tempo): arrampicata

- [ ] Pareti in SurfaceMap, stato Climb, animazione procedurale.
