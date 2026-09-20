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

**Location hints, not click-to-jump.** Confirmed via ILSpy that vanilla's
click-to-jump-to-location on a chirp is Citizen-only:
`ChirpPanel.AddEntry` hardcodes `objectUserData = new InstanceID { Citizen
= message.senderID }`, and `OnTargetClick` only ever reads that — there is
no hook in `IChirperMessage` for a different target type. Getting a real
camera-jump would require a Harmony patch on `ChirpPanel.AddEntry` to
substitute a `NetSegment`-tagged `InstanceID` (confirmed
`InstanceID.NetSegment` exists and works the same way generically) —
deliberately not done, since it would drop the "no load order
requirements" claim for the whole mod, not just this feature, and every
chirp in the game goes through that method (not just ours), so it's shared
patch surface with any other mod touching Chirper.

Instead, `DetailMessage` includes a text location hint via
`LocationDescription.DescribeLocation`: the finding's district name via
`DistrictManager.GetDistrict`/`GetDistrictName` when available, falling
back to an 8-point compass direction from the map center
(`LocationDescription.CompassDirectionFromCenter`) when not — confirmed via
ILSpy that `GetDistrictName` returns `null` for district `0` (the "nothing
painted here" sentinel), not an error or empty string, so the fallback
path is a real, expected case, not defensive-programming-for-a-case-that-
can't-happen. The compass-direction convention (+z north, +x east) is
assumed from standard Unity world axes and has **not** been visually
cross-checked against CS1's actual in-game minimap orientation — treat it
as a rough hint until verified in-game.

## Compatibility

This mod is lower-risk than the overlay mod — it's a read-only diagnostic
pass with no Harmony patches strictly required for v1 — but compatibility
still needs explicit handling, especially since the target audience runs a
heavily modded game.

**No Harmony patches needed for the core diagnostic.** Reading
`NetManager`, `DistrictManager`, and `BuildingManager` buffers doesn't
require intercepting any game method — just iterating public state. Keep it
this way as long as possible; every patch is a new compatibility surface.
The notification surfacing step (milestone 2) turned out not to need one
either — `MessageManager.QueueMessage` is a clean public entry point.

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

**Load order**: this mod doesn't need to load before or after any specific
mod for its core diagnostic to work, since it's pure read-after-the-fact
analysis on a timer, not something hooking a specific simulation step. Note
this explicitly in the Workshop description once published — "no load
order requirements" is a real selling point for a mod aimed at heavily
modded setups.

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
