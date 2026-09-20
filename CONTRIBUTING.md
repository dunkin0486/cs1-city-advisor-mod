# Contributing

## Setup

1. Install the .NET SDK (net35 target, but the modern SDK builds it fine):
   `brew install --cask dotnet-sdk` on macOS.
2. Set `CS1_MANAGED_PATH` to your Cities: Skylines `Managed` assemblies
   folder (see [README.md](README.md#building)).
3. `dotnet build`

## Workflow

- Branch off `main`, open a PR against `main`. Direct pushes to `main` are
  blocked.
- PRs need one approval and all conversations resolved before merging.
- Keep PRs scoped to one diagnostic or one milestone from
  [SPEC.md](SPEC.md) where possible — this repo is small enough that
  reviewing a mixed-purpose PR is harder than reviewing two focused ones.

## Design constraints (see SPEC.md for the full rationale)

- **No Harmony patches for diagnostic logic.** Diagnostics read game state
  (`NetManager`, `DistrictManager`, `BuildingManager`) after the fact; they
  don't intercept simulation methods. Keep it that way unless a diagnostic
  genuinely can't be expressed as a read-only pass. (One Harmony patch does
  exist for the click-to-jump notification enhancement — see SPEC.md
  "Harmony" — but that's a separate, deliberate exception, not a precedent
  for patching diagnostic logic.)
- **No LLM in the diagnosis path.** Detection must stay deterministic
  graph/geometry queries against exact sim data, so a finding is always
  explainable and reproducible. An LLM phrasing layer is a possible future
  add-on for wording variation only — never for detection.
- **New diagnostics implement `IDiagnostic`** (`Run(DiagnosticContext) ->
  List<Finding>`) rather than being bolted onto the scheduler directly.
- **Prefer version-independent game APIs over name matching.** e.g.
  classify roads via `ItemClass.SubService` rather than
  `info.name.Contains("Highway")` once the correct enum value is confirmed
  — road packs like Network Extensions 2 or CSUR don't follow vanilla
  naming, and this mod's stated audience runs heavily modded games.
- **Soft-depend on other mods via `PluginManager` + reflection, never a
  hard assembly reference.** A diagnostic must degrade gracefully (e.g. to
  density-only detection) if an optional companion mod isn't installed.
- **Wrap each diagnostic's `Run()` in try/catch at the call site** so one
  broken diagnostic (e.g. against unusual modded `NetInfo`) can't take down
  the scheduler or other diagnostics.

## Testing changes

`tests/CityAdvisor.Tests.csproj` covers game-independent pure logic (e.g.
`MissingInterchangeScoring`) — run it with `dotnet test tests/`. It has
zero dependency on the CS1 game assemblies by design, since CI (and
anyone without a licensed game install) can't build against them. This
runs automatically on every PR via `.github/workflows/test.yml`.

That intentionally doesn't cover the actual `NetManager`/`DistrictManager`
graph-walking logic, which still depends on real game state. Verify that
by running against a real save and checking the console log or Chirper
feed for `[CityAdvisor] ...` output. Note in your PR description which
save/city layout you tested against and what you saw. When adding new
pure logic (scoring, thresholds, key-building, anything not touching game
types directly), pull it into its own file like
`src/MissingInterchangeScoring.cs` so it can be unit tested the same way.
