# cs1-city-advisor-mod

Cities: Skylines mod. Diagnoses the root cause behind city problems (e.g.
"missing highway interchange") instead of vanilla's generic threshold
warnings. See [SPEC.md](SPEC.md) for the full design and
[CONTRIBUTING.md](CONTRIBUTING.md) for workflow.

## Non-negotiable design constraints

- **No LLM in the diagnosis path.** Detection is deterministic
  graph/geometry queries against exact sim data (`NetManager`,
  `DistrictManager`, `BuildingManager`). Never suggest an LLM call, heuristic
  fuzzing, or probabilistic scoring in place of an exact check — a finding
  must be reproducible and explainable from game state alone. Wording
  variation via LLM is a possible *future* add-on and out of scope now.
- **No Harmony patches for diagnostic logic.** Diagnostics read game state
  after the fact; they don't intercept simulation methods. This still
  holds — `MessageManager.QueueMessage` needed no patch to inject a chirp.
  One patch does exist: `ChirpClickPatch` (`src/ChirpClickPatch.cs`),
  which Postfixes `ChirpPanel.AddEntry` solely to make the click-to-jump
  target our finding's location instead of vanilla's Citizen-only default
  — see SPEC.md "Harmony" for why this one was judged worth it. Don't
  treat that as license to reach for Harmony elsewhere; each new patch is
  its own compatibility surface and its own case to make.
- **Soft dependencies only.** Never add a hard project reference to another
  mod's assembly (e.g. Traffic Flow Overlay). Detect via
  `PluginManager.instance.GetPluginsInfo()` and access its data via
  reflection, wrapped in try/catch that returns null/falls back on any
  failure. A diagnostic must never require a companion mod to function.
- **Don't trust field/API names from memory.** CS1's modding API
  (`NetNode` segment fields, `ItemClass.SubService` values, the
  `ChirpAI`/`IChirperMessage` surface) has shifted across game patches and
  isn't fully documented. Where the skeleton has a `TODO: confirm against
  decompiled <X> for the installed version` comment, treat that as a real
  gap — flag it rather than filling in a plausible-looking API call as if
  verified. If decompiled sources (ILSpy output, etc.) are available in the
  working tree or provided by the user, use them instead of guessing.
- **New diagnostics implement `IDiagnostic`** (`Run(DiagnosticContext) ->
  List<Finding>`) and get wrapped in try/catch at the scheduler call site —
  don't let one diagnostic's exception take down the pass.

## Build

Requires the game's `Managed` assemblies to compile against. Set
`CS1_MANAGED_PATH` to the game's `Managed` folder, then `dotnet build`. See
[README.md](README.md#building). This machine's install path (subject to
change): confirm with
`find ~/Library/Application\ Support/Steam/steamapps/common/Cities_Skylines -iname ICities.dll`
rather than assuming a path.

## Testing

`tests/CityAdvisor.Tests.csproj` unit tests game-independent pure logic
only (e.g. `MissingInterchangeScoring`), runs in CI on every PR
(`.github/workflows/test.yml`), and deliberately has zero reference to the
CS1 game assemblies — CI has no licensed game install to build against.
When adding logic that doesn't need live `NetManager`/`DistrictManager`
state (scoring, thresholds, key formatting), extract it into its own file
and add tests the same way `MissingInterchangeScoring` does.

For the graph-walking diagnostic logic itself, which does depend on real
game state: there's no fixture/mock layer for `NetManager` et al., and
building one (fake `NetNode`/`NetSegment`/`NetAI` stubs to test the
walking logic in isolation) is a real scope decision the user explicitly
deferred when this test project was set up — don't build it unprompted.
Validate that logic by running against a real save instead (see
CONTRIBUTING.md).
