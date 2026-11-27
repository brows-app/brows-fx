# brows-fx

Framework libraries for [Brows](https://github.com/brows-app/brows), published as NuGet packages. They target
.NET Framework 4.6.2 and 4.8, .NET 8 and .NET 10, unless noted below.

## Packages

| Package | Description |
| --- | --- |
| `Brows.Composition` | Discovers and composes implementations of `IExport` with MEF, with variables, initialization, kill and ready-callback lifecycles, and on-demand loading of each composed assembly's private dependencies. |
| `Brows.Composition.Testing` | `ImportSandbox`, for initializing imports in tests from an explicit list of exports. |
| `Brows.Config` | Reads and writes data and configuration objects. |
| `Brows.Config.Json` | Json.NET serializer settings, converters and serialization filters used by the configuration and IPC packages. |
| `Brows.CLI` | Command-line programs built from composed `CliCommand` exports, with console and file logging. |
| `Brows.CLI.About` | An `about` command for `Brows.CLI` programs. |
| `Brows.CLI.Help` | A `help` command for `Brows.CLI` programs. |
| `Brows.IPC` | Object-based calls and posts between processes over TCP connections. |
| `Brows.IPC.Host` | Runs and talks to a host process that serves `Brows.IPC` calls and posts. |
| `Brows.IO.Abstractions` | `IFileSys` and related abstractions over file-system operations, including file open and save selection. |
| `Brows.IO.Abstractions.Testing` | `FileSysSandbox` and helpers for testing code that uses the file-system abstractions. |
| `Brows.IO.Helpers` | Concurrent directory enumeration, path and casing helpers, and file-system task helpers (.NET 8 and .NET 10 only). |
| `Brows.Instantiation` | Creates instances of types derived from a base type, and lookups of them. |

Install a package with the .NET CLI, for example:

```sh
dotnet add package Brows.Composition
```

## Building and testing

The solution is `brows-fx.slnx`, and the SDK version is pinned in `global.json`. The .NET Framework targets build
and run on Windows.

```sh
dotnet build brows-fx.slnx --configuration Release
dotnet test brows-fx.slnx --configuration Release
```

Build output goes under `out/`. The programs under `samples/` show how to use `Brows.CLI` and `Brows.IPC`.

## Releases

Every package is versioned and released together. Publishing a GitHub release runs `.github/workflows/rtm.yml`. It
builds and tests the solution, packs every project under `source/` at the release's version, and pushes the
packages to nuget.org.

## License

[MIT](LICENSE)
