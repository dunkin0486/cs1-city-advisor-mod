# cs1-city-advisor-mod

A Cities: Skylines mod that diagnoses the root cause behind city problems
(e.g. "missing highway interchange") instead of vanilla's generic
threshold warnings ("traffic flow is low").

See [SPEC.md](SPEC.md) for the full design spec: architecture, the first
diagnostic (missing highway interchange), compatibility notes for heavily
modded games, and milestones. Skeleton source lives in
[src/CityAdvisorMod.cs](src/CityAdvisorMod.cs).

## Requirements

- Cities: Skylines (base game)
- .NET SDK (targets `net35`, but the modern SDK builds it fine)
- Optional: the ["Harmony (Mod Dependency)"](https://steamcommunity.com/sharedfiles/filedetails/?id=2040656402)
  Workshop item, for click-to-jump on a finding's Chirper message (also
  required by e.g. Traffic Manager: President Edition, so likely already
  present in a modded setup). Without it, findings still work — same
  Chirper message and text location hint, just no clickable camera jump.

## Building

Compiling requires referencing the game's own assemblies
(`ICities.dll`, `ColossalManaged.dll`, `Assembly-CSharp.dll`, etc.). Set the
`CS1_MANAGED_PATH` environment variable to your game's `Managed` folder
before building (the `.csproj` guesses a default Steam path per-OS
otherwise):

```sh
export CS1_MANAGED_PATH="/path/to/Cities Skylines/Managed"
dotnet build
```

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for setup, workflow, and the
project's design constraints (no Harmony patches for diagnostics, no LLM
in the detection path, soft dependencies on other mods).

## Status

Milestones 1 and 2 done and validated against a real 179k-population
save: the missing-interchange diagnostic fires correctly, and findings
surface as Chirper messages with a text location hint and (with Harmony
present) a clickable camera jump to the exact spot. See SPEC.md
milestones for what's next.
