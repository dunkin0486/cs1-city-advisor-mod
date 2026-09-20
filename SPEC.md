# City Advisor Mod — Design Spec

## Problem

Vanilla advisors report aggregate thresholds ("traffic flow is low") without
diagnosing *why*. Players have to manually inspect the map to find the
actual cause (e.g. missing highway interchange, transit desert, zoning
imbalance).

## Goal

A rule-based diagnostic engine that walks the city's simulation state
periodically and fires specific, actionable messages — starting with one
diagnostic end-to-end, then expanding the library.

**No LLM involved in the diagnosis itself.** The detection logic is
deterministic graph/geometry queries against exact sim data. An LLM-based
phrasing layer is a possible future add-on, purely for varying the wording
of an already-correct diagnosis, and is out of scope for v1.

## First diagnostic: missing highway interchange

**Fires when**: a cluster of high-density, high-traffic-density road
segments exists with no highway on/off-ramp within a threshold distance.

**Data needed**:

- Highway segment identification: **confirmed via ILSpy against
  Assembly-CSharp** — there is no `ItemClass.SubService.Highway` value (an
  earlier assumption in this doc was wrong). Vanilla instead exposes a
  virtual `NetAI.IsHighway()` (default `false`), overridden by `RoadBaseAI`
  to return its `m_highwayRules` field. Classify via
  `segment.Info.m_netAI.IsHighway()` — this is the exact check the game
  itself uses, so any road pack wanting highway behavior already has to set
  `m_highwayRules` correctly. No name-matching fallback needed.
- Interchange/ramp node detection: a node is an interchange point if it has
  at least one highway-classified segment AND at least one non-highway
  segment attached. `NetNode` exposes `CountSegments()`/`GetSegment(int)`
  over its 8-slot `m_segment0..m_segment7` fields (confirmed via ILSpy) —
  use those accessors rather than the raw fields directly.
- Density signal: `NetSegment.m_trafficDensity`, same as the overlay mod.
  If the Traffic Flow Overlay mod's flow-ratio data is available (soft
  dependency, not required), prefer requiring high density AND low flow
  ratio together — cuts false positives vs. density alone.
- Zone density: `DistrictManager` / `ZoneBlock` buffer, or simpler for v1:
  building `m_level` and zone type via `BuildingManager`, to find
  residential/commercial clusters worth caring about (skip diagnosing empty
  zoned land).

**Fires**:

```json
{
  "issue": "missing_interchange",
  "segmentId": "<worst offending segment>",
  "position": "(x, y, z)",
  "nearestRampDistance": "<meters>",
  "severity": "<0..1, derived from density + distance>"
}
```

## Architecture

- `IUserMod` entry point (mod metadata only).
- `LoadingExtensionBase` to know when `NetManager`/`DistrictManager` etc.
  are valid, and to start/stop the periodic diagnostic pass.
- Diagnostic pass runs on a slow timer — **every N in-game days**, not every
  frame or even every few seconds. This is a full-map graph walk; it should
  feel more like "the city runs an audit periodically" than a live overlay.
  Use `SimulationManager.instance.m_currentGameTime` to gate this rather
  than a frame counter, so the cadence is consistent regardless of game
  speed.
- Each diagnostic is its own class implementing a shared `IDiagnostic`
  interface (`Run(DiagnosticContext ctx) -> List<Finding>`), so adding more
  diagnostics later doesn't mean touching the scheduler.
- Findings get deduplicated/throttled — don't re-fire the same finding every
  pass if nothing has changed and the player hasn't acted on it yet. Track a
  simple "already reported, not yet resolved" set keyed by location.

## Surfacing findings

CS1 has existing moddable hooks other notification-style mods use:

- `MessageManager.QueueMessage(MessageBase)` — **confirmed via ILSpy and
  implemented**: this is the entry point mods use to inject a custom chirp
  into the Chirper feed. A `FindingChirpMessage : MessageBase` wraps each
  `Finding`, overriding `GetSenderName`/`GetText`/`GetSenderID` (the
  `ICities.IChirperMessage` surface `MessageBase` implements). `senderID`
  is `0` — there's no "system sender" concept in `ChirpPanel`, so a chirp
  with `senderID = 0` just renders with no clickable citizen target, which
  is correct here, not a placeholder.

**Location hints, plus click-to-jump.** `DetailMessage` includes a text
location hint via `LocationDescription.DescribeLocation`: the finding's
district name via `DistrictManager.GetDistrict`/`GetDistrictName` when
available, falling back to an 8-point compass direction from the map
center (`LocationDescription.CompassDirectionFromCenter`) when not —
confirmed via ILSpy that `GetDistrictName` returns `null` for district `0`
(the "nothing painted here" sentinel), not an error or empty string, so
the fallback path is a real, expected case. The compass-direction
convention (+z north, +x east) is assumed from standard Unity world axes
and has **not** been visually cross-checked against CS1's actual in-game
minimap orientation — treat it as a rough hint until verified in-game.

On top of the text hint, clicking a City Advisor chirp's sender name now
jumps the camera to the finding's location — implemented via a Harmony
patch (`ChirpClickPatch`, in `src/ChirpClickPatch.cs`), after determining
this wasn't reachable any other way. Confirmed via ILSpy that vanilla's
click-to-jump is Citizen-only: `ChirpPanel.AddEntry` hardcodes
`objectUserData = new InstanceID { Citizen = message.senderID }`, and
`OnTargetClick` only ever reads that — no `IChirperMessage` hook exists
for a different target type. Rather than reimplementing `OnTargetClick`'s
camera-jump logic, `ChirpClickPatch` **Postfixes `AddEntry`** and
overwrites the button's `objectUserData` with a `NetSegment`-tagged
`InstanceID` for our messages only, then lets vanilla's unmodified
`OnTargetClick` handle the click — confirmed via ILSpy that
`InstanceManager.IsValid`/`FollowInstance` already fully support
`InstanceType.NetSegment` as a followable target, same as any vanilla
Citizen/Building/etc. target. See "Harmony" below for why this was
initially deferred and what changed.

**Second bug, found only by live testing, not decompilation alone:** the
redirect above was necessary but not sufficient. A live test with debug
logging directly on `CameraController.SetTarget` (Prefix+Postfix reading
its private fields via Harmony's `___field` binding — see git history,
since removed once the root cause was found) showed `SetTarget` was being
called correctly with a valid, genuinely-different `NetSegment` target,
but immediately reverted `m_targetInstance` back to empty afterward. Root
cause: `SetTarget`'s own `GameAreaManager.ClampPoint` branch clamps the
target position into vanilla's recognized "owned" tile grid, and clears
the target entirely if clamping changes the position. This city uses the
81 Tiles mod, and our findings — by design, since the diagnostic hunts for
segments far from any highway interchange — tend to land on tiles 81
Tiles lets you build on but that vanilla's own camera-bounds check
apparently doesn't recognize as owned. (An earlier live test seemed to
rule this out: clicking a *different*, unrelated citizen chirp near the
same map region worked fine. That test wasn't actually equivalent — a
citizen's position when clicked is wherever they currently are, not
necessarily wherever the chirp's subject matter referenced.)

Fix: `ChirpClickCameraBoundsPatch` (also on `OnTargetClick`) temporarily
sets `CameraController.m_unlimitedCamera = true` — a public field,
confirmed via ILSpy, that skips the `ClampPoint` branch entirely — only
while handling a click on one of our `NetSegment`-targeted messages, and
restores its original value immediately after in a Postfix. Scoped this
narrowly (not left on globally) so vanilla's own camera bounds still apply
to everything else.

## Compatibility

This mod's core diagnostic is still a read-only pass with no Harmony
patches — the only patch is `ChirpClickPatch`, added specifically for
click-to-jump (see "Harmony" below). Compatibility still needs explicit
handling, especially since the target audience runs a heavily modded
game.

**No Harmony patches for the core diagnostic, still true.** Reading
`NetManager`, `DistrictManager`, and `BuildingManager` buffers doesn't
require intercepting any game method — just iterating public state. Keep
it this way; every additional patch is a new compatibility surface. The
notification surfacing step (`MessageManager.QueueMessage`) also needed no
patch — only the *click-to-jump enhancement* on top of it did.

**Harmony**: initially deferred (see git history / prior SPEC.md
revisions) specifically because it would drop the "no load order
requirements" claim and add shared patch surface on `ChirpPanel`, a method
any other Chirper-touching mod could also patch. Revisited after
confirming (via web research, not assumption) that Harmony is
near-universal in the CS1 ecosystem and the "duplicate copies" risk is
already solved by convention: mods depend on a single shared "Harmony
(Mod Dependency)" Workshop item (boformer/CitiesHarmony) via the
`CitiesHarmony.API` NuGet package, rather than each bundling their own
`0Harmony.dll`. Traffic Manager: President Edition — already installed in
the save this mod is tested against — requires that same shared
dependency, meaning Harmony is very likely already loaded in any heavily
modded CS1 setup. `CityAdvisorMod.csproj` references `CitiesHarmony.API`
only (not a separate `Lib.Harmony` package — that combination produced a
`CS0433` duplicate-type error at compile time, since `CitiesHarmony.API`
transitively brings its own compile-time Harmony assembly). Patches are
applied via `HarmonyHelper.DoOnHarmonyReady` from `CityAdvisorMod`'s
`OnEnabled()` — confirmed via ILSpy that `OnEnabled`/`OnDisabled` aren't
part of the compiled `ICities.IUserMod` interface on this game version,
but `PluginManager` looks them up by reflection
(`GetType().GetMethod("OnEnabled", ...)`) and invokes them with no
arguments if present, so a public no-arg `void OnEnabled()` method is
required and sufficient. The remaining real risk, not eliminated by any
of this: another mod patching the exact same method
(`ChirpPanel.AddEntry`) could still conflict — narrower and quieter than a
hot method like `RoadBaseAI.UpdateLanes`, but not zero.

**Defensive diagnostic execution**: each `IDiagnostic.Run()` call is already
wrapped in try/catch in the scheduler skeleton — keep that. A malformed
diagnostic (e.g. one that breaks against a modded road pack with unusual
`NetInfo` naming) should log and skip, not take down the whole advisor or
the other diagnostics in the same pass.

**Road pack compatibility for highway classification**: `IsHighwaySegment`
now classifies via `segment.Info.m_netAI.IsHighway()` rather than name
matching. Road packs like Network Extensions 2 or CSUR that want highway
behavior (highway-specific traffic rules and rendering) already have to set
`RoadBaseAI.m_highwayRules` correctly for vanilla to treat their roads as
highways at all, so this stays robust across road packs without any
name-based fallback.

**Soft dependency on the Traffic Flow Overlay mod**: don't hard-reference
its assembly. Detect it at runtime via `PluginManager.instance.GetPluginsInfo()`,
matching on a known assembly/mod name, and only attempt to read its
`FlowRatio` data (via reflection, not a direct type reference) if present.
If it's absent, fall back to density-only detection — degrade gracefully,
don't require it.

**Mods that change simulation behavior**: "Real Time"/"Real Population" and
similar mods materially change what normal density and building occupancy
look like over a day/week. Since this mod's diagnostics fire on a weekly
in-game cadence rather than live, this matters less for flicker/noise but
still affects threshold tuning — plan on thresholds being settings-UI-
configurable rather than hardcoded once past the first working diagnostic.

**Load order**: the core diagnostic still has no load order requirements —
pure read-after-the-fact analysis on a timer, not hooking a specific
simulation step. That claim no longer extends to the whole mod, though:
`ChirpClickPatch` requires the shared Harmony dependency
(boformer/CitiesHarmony) to be present to activate, and depends on
`ChirpPanel.AddEntry`'s current shape — a real, if narrow, load-order/
compatibility surface the core diagnostic doesn't have. Say this
precisely in the Workshop description once published: "no load order
requirements for diagnostics; requires Harmony (Mod Dependency) for
click-to-jump, degrades to text-only location hints without it" — not the
older unqualified "no load order requirements."

## Milestones

1. ~~**Diagnostic-only, console logging**~~ **Done.** Validated against a
   real 179k-population save (Steam Workshop "Alder City") — the diagnostic
   correctly fired on real high-density segments, correctly deduped, and
   correctly picked up a new finding after a highway segment was manually
   deleted to induce a problem.
2. ~~**Wire up notification output**~~ **Done.** `FindingChirpMessage` +
   `MessageManager.QueueMessage` — findings now surface as Chirper messages
   in-game, not just the debug console. Not yet re-validated in a live game
   session (needs a game restart to pick up the new build, then a save
   loaded and time advanced).
3. **Tuning pass** (in progress): the severity formula was already fixed
   once — the original `density * (distance / threshold)` saturated to
   1.00 on every real finding in testing, since real findings were 4-6x
   past the threshold. Replaced with a saturating
   `density * (1 - threshold / distance)` factor that discriminates
   properly. Distance threshold (1500m) and density threshold (0.6) are
   still untuned defaults — need playtesting against a few different city
   layouts (small grid city vs. sprawling highway-heavy city will need
   different defaults).
4. **Second diagnostic**: once the first one is solid end-to-end, the
   `IDiagnostic` interface should make adding a second (e.g. transit desert,
   zoning imbalance) mostly independent work.

## Non-goals for v1

- No LLM phrasing layer (see Problem section).
- No settings UI for diagnostic thresholds yet.
- No cross-save persistence of "already reported" findings — in-memory for
  the current session is fine to start.
- No auto-fix / auto-build suggestions beyond the text message itself.
