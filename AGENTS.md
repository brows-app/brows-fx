# AGENTS.md

Guidance for AI coding agents working in the brows-fx repository.

## What this is

brows-fx is the set of framework libraries that the [Brows](https://github.com/brows-app/brows) application is built
on. Each project under `source/` is published as a NuGet package of the same name: composition, configuration,
command-line programs, IPC, file-system abstractions and helpers, and instantiation helpers.

- Solution: `brows-fx.slnx` at the repository root (XML solution format).
- Consumers take the packages from nuget.org, so a public API change is a change for every consumer. Brows itself
  references them through central package versions in its own repository.
- Every package is released together at one version. `.github/workflows/rtm.yml` runs when a GitHub release is
  published: it sets `VersionPrefix` in `Directory.Build.props` from the release tag, builds, tests, packs every
  project under `source/`, and pushes the packages to nuget.org. The `VersionPrefix` committed in the repository is
  not the published version. Creating a release is the user's action; agents never tag, release, or push.

## Layout

| Path | Purpose |
| --- | --- |
| `source/` | The packaged libraries. `source/Directory.Build.props` makes them packable with XML documentation, symbols and Source Link, and imports the shims below. |
| `tests/` | NUnit test projects, one per library, named `<Library>.Tests`. |
| `tests/components/` | Assemblies that tests build, stage, and load in separate processes, such as the composition fixtures driven by `ComposedDependencyResolutionTest`. They are not test projects and import only the root properties. |
| `samples/` | Runnable sample programs. Some tests launch them as processes. |
| `shared/Brows.Shims` | A shared project compiled into every library: `Lock`, `IsExternalInit`, `SupportedOSPlatformAttribute` and `OSPlatformGuarder` for older target frameworks. |
| `shared/Brows.Shims.Tests` | Tests for the shims and the `BrowsTest` helpers. `tests/Directory.Build.targets` compiles them into each test project that has a matching library under `source/`. |
| `agents/plans`, `agents/reviews` | Plans and reviews written by the planning and review skills. |

Build output goes under `out/`: `out/bin/<Project>/<configuration>_<framework>/` for libraries, and `out/tests`,
`out/samples` and `out/components` for the rest (each folder's `Directory.Build.props` sets `ArtifactsPath`).

## Build settings to know

- Target frameworks are `net462;net48;net8.0;net10.0`, set in the root `Directory.Build.props`. `Brows.IO.Helpers`
  and its tests target only `net8.0;net10.0`, and the test components `net48;net10.0`.
- `LangVersion` is 14.0. The root namespace of every project is `Brows`, and a file's namespace follows its folders,
  so a file in `source/Brows.Composition/Composition/ImportCollections/` is in `Brows.Composition.ImportCollections`.
  The root props declare global `Using` aliases for common `System` types.
- Central package management is on with transitive pinning (`Directory.Packages.props`), and version overrides are
  disabled.
- `.gitattributes` stores text with LF and checks most text files out with CRLF.

Use APIs available on every target framework. The .NET Framework targets are where mistakes show up:

- Use the `#if NET9_0_OR_GREATER` `Lock`/`object` pattern for lock fields, as existing types do.
- With C# 14, extension calls on arrays can bind to span overloads in `MemoryExtensions`. For example,
  `bytes.SequenceEqual(other)` then needs `System.Memory`, which fails to load in the .NET Framework test hosts.
  Call `Enumerable.SequenceEqual(a, b)` explicitly.
- Guard platform-specific code with `OSPlatformGuarder` or `OperatingSystem` checks.

## Workflows and skills

Use these skills (Codex copies in `.agents/skills`, Claude copies in `.claude/skills`):

- Building, validating, and sweeping test projects: `brows-code-building`
  ([Codex](.agents/skills/brows-code-building/SKILL.md), [Claude](.claude/skills/brows-code-building/SKILL.md)).
  CI (`.github/actions/build-and-test`) runs `dotnet restore`, `dotnet build --no-restore --configuration Release`,
  and `dotnet test --no-restore --configuration Release --no-build` from the repository root.
- Writing and running tests: `brows-code-testing`
  ([Codex](.agents/skills/brows-code-testing/SKILL.md), [Claude](.claude/skills/brows-code-testing/SKILL.md)).
- Writing or editing C# and project files: `brows-csharp`
  ([Codex](.agents/skills/brows-csharp/SKILL.md), [Claude](.claude/skills/brows-csharp/SKILL.md)).
- Any Git command that changes the repository or contacts a remote: `brows-git`
  ([Codex](.agents/skills/brows-git/SKILL.md), [Claude](.claude/skills/brows-git/SKILL.md)).
- Planning substantial changes: `brows-code-plan`
  ([Codex](.agents/skills/brows-code-plan/SKILL.md), [Claude](.claude/skills/brows-code-plan/SKILL.md)).
- Reviewing changes: `brows-code-review`
  ([Codex](.agents/skills/brows-code-review/SKILL.md), [Claude](.claude/skills/brows-code-review/SKILL.md)).
- Fixing numbered review findings: `brows-code-fix`
  ([Codex](.agents/skills/brows-code-fix/SKILL.md), [Claude](.claude/skills/brows-code-fix/SKILL.md)).

## Tests

- CI runs on `windows-latest` with `BROWS_TEST_ENV=GitHubAction`. Tests that call
  `BrowsTest.Ignore(BrowsTestEnv.GitHubAction)` are skipped there; they launch sample programs as processes.
- Several tests call `Imports.Init(null, ...)`, which composes every `IExport` in the test assembly's output
  directory. An `IExport` declared in a test assembly must therefore have a parameterless constructor and do
  nothing harmful when another test creates and initializes it.
- Keep test components out of the test project's output. `Brows.Composition.Tests` references them with
  `ReferenceOutputAssembly="false"`, `Private="false"`, `SkipGetTargetFrameworkProperties="true"` and
  `GlobalPropertiesToRemove="TargetFramework"`. That way they build for both of their frameworks, and their
  `Brows.*.dll` files are never composed by the tests above.
- Known failures on Windows as of 2026-11-26, present before the dependency-resolution work:
  - `Brows.CLI.About.Tests` and `Brows.IPC.Host.Tests` fail on `net462` and `net48`, because the sample executable
    they launch is not found.
  - `Brows.CLI.Help.Tests` fails on every framework, because its expected output uses `\n` while the output
    uses `\r\n`.

  Compare against a baseline run before attributing these failures to a change.
