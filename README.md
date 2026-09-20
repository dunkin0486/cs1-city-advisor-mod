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

Milestone 1 in progress: implementing the missing-interchange diagnostic
end-to-end with console-only output, before wiring up in-game
notifications. See SPEC.md milestones for the full sequence.
