# Plan: export dependency resolution

Revision 2 (2026-11-26). It addresses every finding of `brows-private/azure/agents/reviews/2026-11-26_Review-001.md`;
see [Review resolution](#review-resolution).

## Goal and acceptance criteria

Let each Brows plugin ship the NuGet packages it alone needs in a directory named after it, inside
`brows.export/`. Resolve those assemblies at runtime automatically, without the host (`brows.csproj`) or
`Brows.Framework*` referencing the packages.

This plan covers two repositories, and the parts must be implemented in order:

- **Part A (this repository, brows-fx):** `Brows.Composition` resolves the private dependencies of every assembly
  it composes. This is the runtime half. Composition already decides which directories and assemblies are loaded
  (`ComposedImportCollectionFactory`), so it is the only place that knows each composed assembly's path without
  duplicating the `ImportInfo` path table.
- **Part B (Brows, `F:/dev/me/brows-private/deps`, branch `deps`):** the export build copies each plugin's
  private package files into `brows.export/<AssemblyName>/`, and Brows takes the new `Brows.Composition` release.
  This is the build half. It depends on Brows's output layout (`UseCommonOutputDirectory`, `brows.export`, the host
  project), so it cannot live in the package.

The work is done when:

1. Building Brows with today's plugins produces no new subdirectories in `out/<Configuration>/brows.export/`, and
   no `BROWS0001`–`BROWS0004` diagnostics. Every package those plugins use is already shipped by `Brows.Framework` or
   `Brows.Framework.Windows`.
2. A plugin with a package that is not deployed by its references or the host gets `brows.export/<AssemblyName>/`.
   That directory holds the package's runtime assemblies and their transitive ones, plus the native and RID-specific
   files that NuGet selects for `win-x64` (falling back to `win`), flattened into the same directory.
3. A plugin is never left without a file it needs:
   - It omits only package files that are byte-for-byte the same package asset as one deployed by a project it
     references or by the host.
   - A file the host ships with a lower assembly version than the plugin needs fails the build.
4. At runtime, `Brows.Composition` resolves an assembly that ordinary probing cannot find from the dependency
   directories of the composed assemblies. It applies identity, version and highest-candidate rules that colocated
   files cannot bypass.
5. A failed or killed composition leaves no resolver attached. When no dependency directories exist, nothing is
   attached, so behavior is unchanged.
6. A real plugin that calls its private package works from `out/Debug` and from a `publish-brows.ps1` output.

Out of scope:
- isolated `AssemblyLoadContext`s and per-plugin `.deps.json` probing;
- satellite resource assemblies;
- RIDs other than `win-x64` and `win`;
- native libraries that load other native libraries by name (see Risks).

## Current state

- **brows-fx** branch `deps` at `a679241`. The baseline plus this plan (`11ffa6a`) and the repository's agent
  skills (`a679241`, `.agents/skills`). `Brows.Composition` targets `net462;net48;net8.0;net10.0` (root
  `Directory.Build.props`). Every package is versioned by the GitHub release tag (`.github/workflows/rtm.yml`).
  Brows consumes `Brows.Composition` 1.1.0 from nuget.org.
- `source/Brows.Composition/Composition/ImportCollections/ComposedImportCollectionFactory.cs`, `Create` -> `List`,
  loops over each path and pattern. It creates `new DirectoryCatalog(path, searchPattern: pattern)`, a
  `CompositionContainer`, and `ComposeParts`. It disposes `resources` itself only when that loop throws.
- `ImportCollection.Init` (`ImportCollection.cs:168`) awaits `factory.Create`, then `Initialize` and `Populate`.
  If those throw, nothing disposes the collection's `Resources`. `ImportState.Ready` (`ImportState.cs:105`)
  assigns `CollectionCache` after awaiting. `ImportState.Dispose` disposes `CollectionCache?.Resources` only, so a
  `Kill` during an in-flight `Ready` leaks the collection that completes afterwards. With a process-wide event
  handler in `Resources`, both leaks would keep resolving assemblies after the composition is gone.
- MEF's `AssemblyCatalog` loads each composed file with `Assembly.Load(AssemblyName)` and falls back to
  `Assembly.LoadFrom(path)`. On .NET, the first `LoadFrom` installs a runtime handler that satisfies a request
  from the requesting assembly's directory. A private dependency loaded with `LoadFrom` therefore enrolls its
  directory, and a colocated lower version can win without reaching another handler (review #2, reproduced by the
  reviewer). `DirectoryCatalog.LoadedFiles` lists every matching file, including ones that MEF skips because they
  are not valid assemblies.
- **Brows** branch `deps` at `2e2b6ab`. `sln/props/export.build.props` builds every plugin into
  `out/<Configuration>/brows.export/` with `UseCommonOutputDirectory=true`, which skips the
  `_CopyFilesMarkedCopyLocal` copy. Class libraries also default to `CopyLocalLockFileAssemblies=false`
  (`Microsoft.PackageDependencyResolution.targets:36`), so package files are not in `ReferenceCopyLocalPaths` at
  all.
- The host `sln/app/source/brows/brows.csproj` deploys, beyond `Brows.Framework*`, the packages of the app projects
  and `Domore.Logs.Conf`. These are outside a platform-neutral export's reference closure. Every managed project
  imports `sln/props/root.build.props`. `Brows.Framework.Program.ImportInfo` composes `"."` and `"brows.export"`
  with `Brows.*.dll` (`sln/framework/source/Brows.Framework/Programs/Program.cs:26`). Brows pins
  `Azure.Storage.Blobs` 12.28.0 in `sln/Directory.Packages.props`.
- No change is needed for deployment. `publish-brows.ps1` `Copy-Directory` copies recursively, and brows-release
  `Packaging/Directory.cs` harvests subdirectories. Both were checked by reading the source; neither has been run.
- The Azure plan on the Brows `azure` branch works around the gap by adding the Azure SDK packages to
  `brows.csproj`. That is the coupling this plan removes.

### Verified by prototypes (2026-11-26, SDK 10.0.404, net10.0, outside both repositories)

- **First prototype (runtime):** the convention, a global index and `AppDomain.AssemblyResolve`.
  - Azure (Blobs 12.30.2), transitive and SQLite assemblies loaded from the plugin's directory.
  - A host-shipped package loaded from the host directory.
  - The native `e_sqlite3.dll` loaded beside its managed caller through default `DllImportSearchPath.AssemblyDirectory`
    probing.
  - A second plugin resolved the first plugin's private assembly.
  - Without the resolver, composition failed.
  - The runtime accepts a lower version than requested if a handler returns one, so the resolver must enforce
    versions itself.
- **Second prototype (the Part B build logic below):** RoslynCodeTaskFactory with a `.cs` source file, the host query
  with `DisablePackageAssetsCache=true`, and local probe packages.
  - A package with `runtimes/win-x64/native/Preferred.dll` and `runtimes/win/native/Fallback.dll` yields only
    `Preferred.dll`, and a `win`-only package falls back to `win`.
  - A plugin referencing another plugin at a higher `Azure.Storage.Blobs` (12.30.2 over 12.25.0) ships its own newer
    files.
  - A host with a higher `Microsoft.Bcl.AsyncInterfaces` (10.0.13 over 6.0.0 and 10.0.9) made both plugins drop
    their copies.
  - A host with a lower one (6.0.0 under 10.0.9) failed the build with `BROWS0002`.
  - Newtonsoft.Json 13.0.4 against a host's 13.0.3 (both assembly version 13.0.0.0) produced warning `BROWS0003`.
  - The task compiled with C# 14 syntax: file-scoped namespace, `is not null`, collection expressions.
  - Visual Studio MSBuild was not tried.

The fixed ownership and selection rules below come from these runs. The .NET Framework runtime path has not been
run yet. Part A's integration components exercise it.

## Proposed design

### Convention

An assembly composed from a file `D/X.dll` may have a sibling directory `D/X/` that holds its private
dependencies. Every file that a catalog matched (`DirectoryCatalog.LoadedFiles`) counts, even one that MEF then
skips as invalid: such a directory only makes assemblies resolvable, and ignoring it would depend on MEF internals.
Only `.dll` files directly in `D/X/` are candidates. The convention applies to every composed path, so `"."` and
`brows.export` behave the same.

Ordinary probing always comes first. On .NET that is the TPA list and any `AssemblyLoadContext.Resolving` handler,
and on .NET Framework the Load-context probing of the application base. The resolver only answers binds that
ordinary probing could not satisfy. Detaching it does not unload assemblies it already loaded.

### Part A: brows-fx

#### `ComposedDependencyResolver`

New internal type `Brows.Composition.ImportCollections.ComposedDependencyResolver : IDisposable` in
`source/Brows.Composition/Composition/ImportCollections/ComposedDependencyResolver.cs`.

- **Constructors.**
  - `internal ComposedDependencyResolver(IEnumerable<string> composedFiles)` chains to the testing constructor with
    `AssemblyName.GetAssemblyName` and the private static `Load`.
  - The testing constructor is `internal ComposedDependencyResolver(IEnumerable<string> composedFiles,
    Func<string, AssemblyName> nameOf, Func<string, Assembly> load)`.
  - Construction only enumerates directories; it reads no metadata and attaches nothing.
- **Index.**
  - For each distinct composed file (full path, ignoring case), look in `Path.Combine(Path.GetDirectoryName(file),
    Path.GetFileNameWithoutExtension(file))`.
  - Add the top-level files of that directory whose `Path.GetExtension` is `.dll`, ignoring case.
  - Key each file by its name without extension, ignoring case, across all directories, so that one plugin can use
    another's private files.
  - Each key maps to a private nested `Entry`, which holds its paths in ordinal, case-insensitive order.
  - `internal bool HasDependencies` is true when the index is non-empty.
- **Candidates (once per entry, lazily, under the entry's lock).**
  - Read `nameOf(path)` for each path.
  - Skip `BadImageFormatException` silently, since native DLLs are expected.
  - Skip any other exception with a warning.
  - Keep only names that equal the key (ignoring case) and are culture-neutral.
- **`internal Assembly Resolve(AssemblyName name)`** works under the entry's lock (one `Lock`/`object` per entry,
  using the `#if NET9_0_OR_GREATER` pattern of `ImportCollection`):
  1. Return null if `name.Name` is empty, `name.CultureName` is not empty (a satellite), or the key is not indexed.
  2. A candidate is compatible when its public key token matches the requested one:
     - when `name.GetPublicKeyToken()` is null (a simple-name request), any token is accepted;
     - when it is empty, the candidate must be unsigned;
     - otherwise the tokens must be equal.
  3. If the entry already loaded a candidate, it is the only possible answer, because the process holds one assembly
     per simple name. Return it if it is compatible and its version is at least `name.Version`. Otherwise log a
     warning and return null.
  4. Otherwise choose, among compatible candidates that have not failed, the highest `Version` (null counts as
     `0.0.0.0`), with ties broken by path order. If none, return null. If its version is lower than
     `name.Version`, log a warning and return null without loading.
  5. Load the chosen candidate with `load(path)`. If loading throws, or the loaded assembly's name, version or token
     differs from the candidate's, log an error, mark the candidate failed and return null. A failed candidate is
     never tried again. Otherwise record it as loaded, log its name and path once, and return it.
- **`private static Assembly Load(string path)`**: `AssemblyLoadContext.Default.LoadFromAssemblyPath(path)` under
  `#if NETCOREAPP`, and `Assembly.LoadFile(path)` otherwise. Neither enrolls the directory in the runtime's
  same-directory fallback, so every dependency of a private assembly comes back to this resolver (review #2). On
  .NET Framework, `LoadFile` assemblies are found by later binds only through this resolver, which returns the
  instance it recorded in step 3.
- **Events.**
  - `internal void Attach()` adds a handler to `AppDomain.CurrentDomain.AssemblyResolve` when `HasDependencies` is
    true and the resolver is neither attached nor disposed.
  - `IDisposable.Dispose` (explicit) removes it and marks the resolver disposed. It is idempotent.
  - Both take the same lock, so add and remove cannot interleave.
  - The handler parses `args.Name` with `new AssemblyName(...)`, returns null on a parse failure (logged), and
    never throws.
  - `AssemblyResolve` exists on all four target frameworks.

Member layout follows the C# conventions: static `Log`, private fields, private methods, internal members,
explicit `IDisposable.Dispose`, then the nested `Entry` and `Candidate` types. Do not name a member `Path`, which
would hide `System.IO.Path`; use `FilePath`.

#### Factory change

Split `List` in `ComposedImportCollectionFactory` into two phases, so that every catalog's files are indexed before
any type is inspected:

```text
catalogs = []
for each (path, pattern):                        // unchanged logging and Directory.Exists check
    catalog = new DirectoryCatalog(path, pattern); resources.Add(catalog); catalogs.Add(catalog)
resolver = new ComposedDependencyResolver(catalogs.SelectMany(c => c.LoadedFiles))
resources.Add(resolver); resolver.Attach()        // the only Attach call, before the collection is published
for each catalog in catalogs:                    // unchanged compose, ReflectionTypeLoadException logging, export logging
    container = new CompositionContainer(catalog); resources.Add(container); container.ComposeParts(part); exports.AddRange(...)
```

The export order, injected exports, logging, and the existing cleanup on failure are preserved.

#### Collection lifetime

- Add `internal void DisposeResources()` to `ImportCollection`. It disposes `Resources` in reverse order, catching and
  logging each exception, and runs once (`Interlocked` flag). Move the loop in `ImportState.Dispose` into it, and call
  `CollectionCache?.DisposeResources()` from there.
- In `ImportCollection.Init`, wrap everything after `factory.Create` (variables, `Initialize`, `Populate`) in
  `try`/`catch`. On any exception, including cancellation, call `collection.DisposeResources()` and rethrow with
  `throw;`.
- In `ImportState.Ready`, after `await CollectionTask`, take `Locker`. If `Disposed` is set, release the lock, call
  `collection.DisposeResources()` and throw `DisposedException()`. Otherwise assign `CollectionCache` and return it.
  `DisposeResources` is idempotent, so concurrent awaiters are safe.

Document the convention on the public API with a `<remarks>` on `ImportInfo.Composed`
(`source/Brows.Composition/ImportInfo.cs`): "An assembly composed from a path can keep its private dependencies in
a directory beside it, named for the assembly file without its extension. Assemblies in that directory are loaded
on demand when ordinary probing does not find them."

### Part B: Brows

The build decides, for each plugin, which package files it must ship. The rules need joins and version comparison,
so they live in a small inline task; MSBuild only gathers inputs and copies.

1. **`sln/props/root.build.props`**: add a query target that returns a project's raw package runtime files. It
   reads only the assets file, so it never builds anything.

   ```xml
   <!-- Returns the package runtime files of this project's closure. See _Brows_Target_CopyExportDependencies. -->
   <Target Name="_Brows_Target_GetPackageRuntimeFiles"
           DependsOnTargets="ResolvePackageAssets"
           Returns="@(_Brows_Item_PackageRuntimeFile)">
     <ItemGroup>
       <_Brows_Item_PackageRuntimeFile Include="@(RuntimeCopyLocalItems);@(NativeCopyLocalItems);@(RuntimeTargetsCopyLocalItems)" />
     </ItemGroup>
   </Target>
   ```

2. **`sln/props/SelectExportDependencies.cs`** (new): `Brows.Build.SelectExportDependencies :
   Microsoft.Build.Utilities.Task`, compiled by RoslynCodeTaskFactory. It follows the C# conventions; it is not part
   of any project.
   - Inputs:
     - `Project` (string);
     - `RuntimeIdentifiers` (`win-x64;win`);
     - `Candidates`: the plugin's `ReferenceCopyLocalPaths` with `AssetType` `runtime` or `native`, after the SDK's
       conflict resolution;
     - `Referenced`: the query output of its project references;
     - `Host`: the query output of `brows.csproj`.
   - Output: `Dependencies`, each item with `DestinationFileName` metadata.
   - **Group selection**, applied to all three inputs. Group items by `NuGetPackageId` and `AssetType`. Within a group,
     keep the items whose `RuntimeIdentifier` is `win-x64` if any exist, else `win`, else the RID-agnostic items.
     This is NuGet's per-package asset-group selection (review #5).
   - **Flattening**: two selected candidates with the same file name and different paths are error `BROWS0001`.
   - **Ownership**, for each selected candidate:
     - If a selected `Referenced` or `Host` item has the same full path, omit it. It is the same package file, and
       its owner deploys it: the host, or the referenced export (recursively, by the same rule).
     - Else, if a selected `Host` item has the same file name, compare `AssemblyName` and file versions:
       - If both are managed, the tokens are equal, and the host's assembly version is at least the candidate's,
         omit the candidate.
       - If the assembly versions are equal and the host's `FileVersionInfo.FileVersion` is lower, also emit warning
         `BROWS0003`, because the host's older build is the one that loads.
       - Otherwise emit error `BROWS0002`. It names both files, both packages and both versions, and says to align
         the versions (a direct `PackageReference` or a `Directory.Packages.props` pin). The default context holds
         one version per name, and the host's always wins.
     - Else ship it. A same-named file of a different version owned by a referenced export is shipped too, and at
       runtime the highest version wins (review #1).
   - Use the `Log.LogError`/`LogWarning` overloads that take a code, so the diagnostics are filterable.
3. **`sln/props/export.build.props`**:
   - Set `CopyLocalLockFileAssemblies=true` (not `EnableDynamicLoading`, which also emits a runtimeconfig per plugin).
   - Set `_Brows_Property_HostProject` to `$(SolutionDir)app/source/brows/brows.csproj` and
     `_Brows_Property_HostAssetsFile` to `$(SolutionDir)app/source/brows/obj/project.assets.json`.
   - Add the `UsingTask` (`TaskName="Brows.Build.SelectExportDependencies"`, `TaskFactory="RoslynCodeTaskFactory"`,
     `AssemblyFile="$(MSBuildToolsPath)\Microsoft.Build.Tasks.Core.dll"`, `<Code Type="Class" Language="cs"
     Source="$(MSBuildThisFileDirectory)SelectExportDependencies.cs" />`).
   - Add the target:

   ```xml
   <Target Name="_Brows_Target_CopyExportDependencies"
           AfterTargets="CopyFilesToOutputDirectory"
           DependsOnTargets="ResolveReferences"
           Condition="'$(DesignTimeBuild)' != 'true'">
     <MSBuild Projects="@(_MSBuildProjectReferenceExistent)"
              Targets="_Brows_Target_GetPackageRuntimeFiles"
              BuildInParallel="$(BuildInParallel)"
              Properties="%(_MSBuildProjectReferenceExistent.SetConfiguration); %(_MSBuildProjectReferenceExistent.SetPlatform); %(_MSBuildProjectReferenceExistent.SetTargetFramework)"
              RemoveProperties="%(_MSBuildProjectReferenceExistent.GlobalPropertiesToRemove)$(_GlobalPropertiesToRemoveFromProjectReferences)"
              SkipNonexistentTargets="true"
              Condition="'@(_MSBuildProjectReferenceExistent)' != ''">
       <Output TaskParameter="TargetOutputs" ItemName="_Brows_Item_ReferencedPackageRuntimeFile" />
     </MSBuild>
     <MSBuild Projects="$(_Brows_Property_HostProject)"
              Targets="_Brows_Target_GetPackageRuntimeFiles"
              Properties="DisablePackageAssetsCache=true"
              Condition="Exists('$(_Brows_Property_HostAssetsFile)')">
       <Output TaskParameter="TargetOutputs" ItemName="_Brows_Item_HostPackageRuntimeFile" />
     </MSBuild>
     <Warning Code="BROWS0004"
              Text="$(MSBuildProjectName): the host project is not restored, so files it ships may be copied into brows.export\$(AssemblyName). Restore the solution."
              Condition="!Exists('$(_Brows_Property_HostAssetsFile)')" />
     <ItemGroup>
       <_Brows_Item_PackageCopyLocal Include="@(ReferenceCopyLocalPaths)"
                                     Condition="'%(ReferenceCopyLocalPaths.AssetType)' == 'runtime' or '%(ReferenceCopyLocalPaths.AssetType)' == 'native'" />
     </ItemGroup>
     <SelectExportDependencies Project="$(MSBuildProjectName)"
                               RuntimeIdentifiers="win-x64;win"
                               Candidates="@(_Brows_Item_PackageCopyLocal)"
                               Referenced="@(_Brows_Item_ReferencedPackageRuntimeFile)"
                               Host="@(_Brows_Item_HostPackageRuntimeFile)">
       <Output TaskParameter="Dependencies" ItemName="_Brows_Item_ExportDependency" />
     </SelectExportDependencies>
     <Copy SourceFiles="@(_Brows_Item_ExportDependency)"
           DestinationFiles="@(_Brows_Item_ExportDependency->'$(OutDir)$(AssemblyName)\%(DestinationFileName)')"
           SkipUnchangedFiles="$(SkipCopyUnchangedFiles)"
           Retries="$(CopyRetryCount)"
           RetryDelayMilliseconds="$(CopyRetryDelayMilliseconds)">
       <Output TaskParameter="DestinationFiles" ItemName="FileWrites" />
     </Copy>
   </Target>
   ```

   Design notes:
   - **Reference query.** It mirrors the SDK's
     `_GetCopyToOutputDirectoryItemsFromTransitiveProjectReferences` call, so it reuses the project instances that
     `ResolveProjectReferences` already built. Querying direct references is enough, because each reference's
     closure contains its transitive packages. A raw reference item can only remove an identical candidate, and an
     identical file goes through the same group selection and conflict resolution in both projects, so raw items
     are safe for this comparison.
   - **Host query.** Every plugin calls it with the same global properties plus `DisablePackageAssetsCache=true`,
     so MSBuild evaluates `brows.csproj` once per build. That evaluation never writes the host's
     `obj/brows.assets.cache`, so it cannot race the host's own build.
   - **File writes.** `FileWrites` lets incremental clean remove copies when a package is dropped. The reviewer
     confirmed this under `UseCommonOutputDirectory=true` in a scratch build.
   - **Not covered.** Satellite (`resources`) assets are not copied. A file name present in the host closure under a
     RID other than `win-x64`/`win` is not compared.
4. **`sln/Directory.Packages.props`**: after the Part A release, raise `Brows.Composition` and
   `Brows.Composition.Testing` to it. Leave the other brows-fx packages alone.
5. **`AGENTS.md`** (repository root of the Brows checkout):
   - In **Layout**, extend "Build output goes to ..." with: "and each plugin's private NuGet dependencies in
     `brows.export/<AssemblyName>/`."
   - In **Composition**, add a bullet:

     > A plugin may reference NuGet packages that the framework and host do not. The export build copies the files
     > that no project reference or the host already deploys into `brows.export/<AssemblyName>/`, and
     > `Brows.Composition` loads them on demand. A file the host ships at a lower assembly version fails the build
     > (`BROWS0002`); align the versions instead of suppressing it. Do not add a plugin's packages to `brows.csproj`
     > or `Brows.Framework*`. Assemblies in those directories are not composed, so a package that contains exports
     > must be referenced by the framework.

Nothing in `Brows.Framework` code changes. `Program` already composes through `ImportInfo.Composed`.

## Implementation steps

### Part A (brows-fx, worktree `F:/dev/me/brows-fx-deps`)

1. **Add `ComposedDependencyResolver`** as designed. It needs no new package references. *Done when*
   `source/Brows.Composition` builds for all four target frameworks with no new warnings.
2. **Restructure `ComposedImportCollectionFactory.Create`** into the two phases, with the single `Attach`. Depends
   on step 1.
3. **Fix the collection lifetime** (`ImportCollection.DisposeResources`, the `Init` catch, the `ImportState.Ready`
   check after awaiting). *Done when* steps 2–3 leave the existing `Brows.Composition.Tests` passing unchanged.
4. **Document the convention** in `ImportInfo.Composed` `<remarks>`.
5. **Unit tests.** Use the testing constructor and a unique temporary directory per test, deleted in `TearDown`.
   Use empty placeholder files. `nameOf` maps each path to a configured `AssemblyName` or throws. `load` records
   its path and returns an assembly whose name is configured to match, or a mismatching one for the identity test.
   `InternalsVisibleTo("Brows.Composition.Tests")` exists. Add
   `tests/Brows.Composition.Tests/Composition/ImportCollections/ComposedDependencyResolverTest.cs`
   (`[TestFixture] internal sealed`):

   | Test | Setup | Expected |
   | --- | --- | --- |
   | `Resolve_DependencyBesideComposedFile_LoadsIt` | `root/P.dll`, `root/P/Dep.dll` (1.0) | Loaded; `load` called once with `root/P/Dep.dll` |
   | `Resolve_SameNameTwice_ReturnsSameAssembly` | as above, twice | Same instance; one `load` |
   | `Resolve_SeveralDirectories_LoadsHighestVersion` | `A/Dep.dll` 1.0, `B/Dep.dll` 2.0 | `B/Dep.dll` for requests of 1.0, 2.0 and no version |
   | `Resolve_RequestedVersionAboveAvailable_ReturnsNull` | Dep 1.0, request 2.0 | null; `load` not called |
   | `Resolve_AfterLoad_LaterHigherRequestReturnsNull` | Dep 1.0 loaded, then request 2.0 | null, no second `load` |
   | `Resolve_PublicKeyToken_FiltersBeforeRanking` | `A/Dep.dll` 1.0 token T, `B/Dep.dll` 2.0 token U; request token T | `A/Dep.dll`; with only `B`, null |
   | `Resolve_UnsignedRequest_RejectsSigned` | candidate with token T; request with empty token | null |
   | `Resolve_CultureSpecific_ReturnsNull` | request `Dep, Culture=fr`; candidate with culture `fr` and neutral request | null for both |
   | `Resolve_UnknownOrComposedName_ReturnsNull` | request `Other` and `P` | null |
   | `Resolve_NativeFile_IsSkipped` | `A/Dep.dll` throws `BadImageFormatException`, `B/Dep.dll` 1.0 | `B/Dep.dll` |
   | `Resolve_NameMismatch_ReturnsNull` | `P/Dep.dll` whose metadata name is `Other` | null |
   | `Resolve_LoadedIdentityMismatch_ReturnsNull` | `load` returns an assembly of a different version | null; candidate not retried |
   | `Resolve_LoadThrows_TriesNextCandidateOnce` | `B` 2.0 throws, `A` 1.0 loads | first call `A`; `B` never retried |
   | `Constructor_NestedOrMissingDirectories_AreNotIndexed` | `P/sub/Dep.dll`; composed `Q.dll` with no `Q/` | `HasDependencies` false |
   | `Dispose_BeforeOrAfterAttach_IsIdempotent` | attach, dispose twice; dispose then attach | no exception |

   In `ImportsTest_Kill` (or a new `ImportStateTest`), with an internal `ImportCollectionFactory` stub that returns
   a collection with a recording `IDisposable` resource:
   - `Init_ExportInitThrows_DisposesResources`: an `IExportAndInit` throws. Expect the resource disposed once.
   - `Init_Canceled_DisposesResources`: the token is canceled during `Init`. Expect the resource disposed once.
   - `Kill_WhileReadyInFlight_DisposesResources`: a gated `IExportAndInit` is released after `Kill`. Expect the
     resource disposed once and `Ready` to throw `ObjectDisposedException`.
6. **Integration components (review #6).** Add `tests/components/Directory.Build.props`, which imports the root props
   only (not the test props), with `<TargetFrameworks>net48;net10.0</TargetFrameworks>` and `IsPackable=false`. Add
   these projects, each with `OutputPath` `$(MSBuildThisFileDirectory)../../out/components/$(Configuration)/$(TargetFramework)/$(MSBuildProjectName)/`:

   | Project | Content |
   | --- | --- |
   | `Brows.Composition.Component.Library` | `public static class ComponentLibrary { public static string Location => ...Assembly.Location; }`, `AssemblyVersion` 2.0.0.0 |
   | `Brows.Composition.Component.Library.V1` | The same source (`Compile Include` of the Library file), `AssemblyName` `Brows.Composition.Component.Library`, `AssemblyVersion` 1.0.0.0 |
   | `Brows.Composition.Component.Middle` | `public abstract class ComponentMiddleBase { public static string LibraryLocation() => ComponentLibrary.Location; }`; references Library |
   | `Brows.Composition.Component.Export` | `ComponentExport : ComponentMiddleBase, IExportAndInit` (its base type is needed during MEF discovery); `Init` throws when `BROWS_COMPONENT_FAIL_INIT=1`; `Probe()` returns `LibraryLocation()` and Middle's location. References Brows.Composition, and Middle and Library with `Private=false` |
   | `Brows.Composition.Component.Consumer` | `ComponentConsumer : IExport`, `Probe()` returns `ComponentLibrary.Location` (needed only after startup). References Brows.Composition and Library (`Private=false`) |
   | `Brows.Composition.Component.Host` | Exe, `Host <root> [--probe] [--kill]`. Composes `ImportInfo.Composed` with `{root}/a` and `{root}/b`, each with `Brows.Composition.Component.*.dll`, through `Imports.Init`, and prints the composed export types or the exception. With `--probe`, it calls `Probe()` on `ComponentExport` first and then on `ComponentConsumer`, printing each result or exception. The order matters: Middle must be the first to request Library, so that the competing-versions case exercises review #2. With `--kill`, it calls `Kill`. Finally it calls `Assembly.Load("Brows.Composition.Component.Library")` and prints the location or the exception |

   `Brows.Composition.Tests` references them with `ReferenceOutputAssembly="false"` and
   `SkipGetTargetFrameworkProperties="true"`, so they build for both frameworks first. It passes the output root
   through an `AssemblyMetadata("BrowsComponentOutput", "$(MSBuildThisFileDirectory)..\..\out\components\$(Configuration)\")`
   item.

   Add `tests/Brows.Composition.Tests/Composition/ImportCollections/ComposedDependencyResolutionTest.cs`. Compile it
   for the net10.0 test target only (`#if NET10_0`), and parameterize every case over the `net48` (`Host.exe`) and
   `net10.0` (`dotnet Host.dll`) hosts. Each case stages a fresh temporary root by copying component outputs, runs the
   host in a new process with a timeout, and asserts the printed locations. "Under X" means the path starts with X.

   | Case | Layout and options | Expected |
   | --- | --- | --- |
   | Baseline | `a/Export.dll` only | Composition fails, naming Middle |
   | Discovery and startup | `a/Export.dll`, `a/Brows.Composition.Component.Export/{Middle, Library}`, `b/Consumer.dll`; `--probe` | Middle and Library under `a/…Export/`; Consumer's Library under `a/…Export/` (another catalog) |
   | Competing versions | `a/Export.dll`, `a/…Export/{Middle, Library.V1 output}`, `b/Consumer.dll`, `b/…Consumer/{Library}`; `--probe` | Library under `b/…Consumer/` for both probes (highest; the colocated 1.0 is not used) |
   | Lower only | `a/Export.dll`, `a/…Export/{Middle, Library.V1 output}`; `--probe` | Export composes; its `Probe()` fails to load Library 2.0; the final `Assembly.Load` does not return the 1.0 file |
   | Failed init | Discovery layout, `BROWS_COMPONENT_FAIL_INIT=1`, no `--probe` | Init fails; the final `Assembly.Load` of Library fails |
   | Kill | Discovery layout, `--kill`, no `--probe` | The final `Assembly.Load` of Library fails |
   | Kept alive | Discovery layout, no options | The final `Assembly.Load` returns Library under `a/…Export/`; this is the control for the two cases above |

7. **Run the solution's tests** as CI does, from `F:/dev/me/brows-fx-deps`: `dotnet restore`, then
   `dotnet build --no-restore --configuration Release`, then
   `dotnet test --no-restore --configuration Release --no-build`. If the .NET Framework targeting packs or runtime
   are missing, report it as blocked; do not skip.

Part A is then ready for the user to commit and merge. Do not tag, release, or push; a GitHub release tag such as
`1.2.0` is the user's action.

### Part B (Brows, worktree `F:/dev/me/brows-private/deps`)

8. **Add the query target, task file and copy target** as specified. Keep `.props` files UTF-8 with BOM, CRLF,
   and two-space indentation, and the `.cs` file per the C# conventions.
9. **Validate the layout** with the `brows-code-building` skill (full solution, Visual Studio MSBuild, Debug|x64).
   This also checks the RoslynCodeTaskFactory task under Visual Studio MSBuild.
   - Record the `sln/out/Debug/brows.export` subdirectories before the change and after it. They must be identical,
     with no `BROWS*` diagnostics. A `BROWS0002` or `BROWS0003` on an existing plugin is a real pre-existing
     version mismatch: stop and report it; do not suppress it.
   - Temporarily (do not commit), add a `PackageReference` to `Azure.Storage.Blobs` (the checkout pins 12.28.0) to
     `sln/export/source/Brows.Url/Brows.Url.csproj` and rebuild.
     - `brows.export/Brows.Url/` must hold exactly the selected `runtime` and `native` files of that package's
       closure in `obj/project.assets.json` (target `net10.0`), minus those the host or `Brows.Framework` deploys.
       Compare the lists rather than hard-coding them.
     - `Brows.FTP.Client`, `Brows.FTP` and `Brows.FTPS`, which reference `Brows.Url`, must get no directory.
     - Remove the reference and rebuild. `brows.export/Brows.Url/` must have no files left, though an empty directory
       is acceptable. A stale non-empty directory is a failure, because the resolver could select a stale binary.
10. **Runtime check before the release (review #6).**
    - Pack Part A locally: `dotnet pack` of `source/Brows.Composition` and `source/Brows.Composition.Testing`, with
      `-c Release -p:VersionPrefix=1.2.0-local.1 -o <scratch feed>`, run from the brows-fx worktree.
    - In the Brows worktree, use a temporary `sln/nuget.config` (not committed) that adds the scratch feed and maps
      `Brows.Composition*` to it. Package source mapping is enabled in the user-level config. Temporarily set both
      versions to `1.2.0-local.1`.
    - Add a temporary, uncommitted plugin `sln/export/source/Brows.DependencyProbe`. It references
      `Azure.Storage.Blobs` and has an `IExportAndInit` whose `Init` constructs a `BlobContainerClient` for
      `https://example.invalid/c` without any request, and logs `typeof(BlobContainerClient).Assembly.Location`.
      Register it in `brows.slnx` temporarily.
    - Build. Start Brows from `out/Debug` (`brows-app-running`); the log must show the location under
      `brows.export/Brows.DependencyProbe/`. Then run `publish-brows.ps1` to a scratch output, start that
      `brows.exe`, and check the same.
    - MSI: if brows-release can package a local publish output, confirm that the harvested component list contains
      `brows.export/Brows.DependencyProbe/*`. Otherwise record it as verified by source inspection only.
    - Revert all temporary changes. Delete the `brows.composition` and `brows.composition.testing` `1.2.0-local.1`
      folders from the global packages folder.
11. **After the release**, raise the two package versions in `sln/Directory.Packages.props`, restore and build.
    *Done when* the solution builds, the test sweep from `brows-code-building` matches its baseline, and Brows
    starts with `Composing parts...` for both paths and no loader exceptions in its log.
12. **Update `AGENTS.md`** as specified.

## Risks and decisions

- **One version per assembly name (decided).** Everything loads into the default context, which keeps cross-plugin
  type identity and MEF unchanged. The host's assemblies always win; the build fails rather than ship a newer copy
  of one (`BROWS0002`). Private packages shared by several plugins resolve to the highest version present. A plugin
  compiled against a lower version then runs on the higher one, as NuGet unification would do in a single app.
- **Requested version above the available or loaded one returns null (decided).** The runtime would accept the
  lower version and fail later with `MissingMethodException`. A logged null fails at the reference instead.
- **Strictness on equal assembly versions.** Packages such as Newtonsoft.Json keep one assembly version across
  releases. The host's older file then wins, with warning `BROWS0003`. This matches what the binder does. Making it
  an error would reject safe patch differences.
- **Inline task.** RoslynCodeTaskFactory compiles `SelectExportDependencies.cs` on first use in each build process.
  It has been verified with `dotnet build` 10.0.404 only; step 9 verifies Visual Studio MSBuild.
- **`AssemblyResolve` is process-wide.** On .NET it also fires for failed binds in custom load contexts, and the
  resolver loads into the default context. Neither Brows nor `Brows.Composition` uses custom contexts.
- **Native libraries.** Supported: a managed package assembly that calls a native library which is selected into
  the same directory, as with SQLite (verified). Unverified and unsupported: a native library that loads another
  native library by name, and native files reached any other way.
- **RIDs are fixed to `win-x64` and `win`.** ARM64 (mentioned in the app-updates plan) will need them derived from
  the platform.
- **Exports inside dependency directories are not composed.**
- **Pre-existing, not changed here:** `ComposedImportCollectionFactory` checks `Directory.Exists(path)` relative to
  the current directory, while `DirectoryCatalog` resolves relative paths against `AppDomain.BaseDirectory`, and
  Brows never sets the current directory. Starting `brows.exe` from another working directory may skip
  `brows.export`. This was found by reading the code and has not been reproduced. It is a candidate follow-up in
  this repository.
- **Follow-up for the Azure plan:** after this lands, drop its `brows.csproj` package references and its "no
  `Azure.*.dll` in `brows.export/`" check. Its third-party notices are still required.

## Review resolution

Decisions on `2026-11-26_Review-001.md`. All six findings changed the plan.

| # | Decision | Where |
| --- | --- | --- |
| 1 | Accepted. Omission now needs an identical package file from a reference or from the queried host. Host name collisions are compared by assembly version, with errors and warnings. A different version owned by an export is shipped. Prototyped. | Part B 1–3, step 9 |
| 2 | Accepted. Private files load through `AssemblyLoadContext.Default.LoadFromAssemblyPath` on .NET and `Assembly.LoadFile` on .NET Framework, and the loaded identity is verified. The "Competing versions" component covers the bypass. | Resolver `Load`, step 6 |
| 3 | Accepted; confirmed in `ImportCollection.Init` and `ImportState.Ready`. Resources are disposed on failed init and on `Kill` during init. The single `Attach` and `Dispose` share a lock. | Collection lifetime, steps 3, 5, 6 |
| 4 | Accepted. Culture and public key token filter candidates before ranking. A loaded result is rechecked against later requests. | Resolver, step 5 |
| 5 | Accepted. Per-package, per-asset-type RID group selection, with flattening collisions as `BROWS0001`. Prototyped with the reviewer's Preferred/Fallback layout. | Part B 2 |
| 6 | Accepted. Compiled components run in fresh processes on net48 and net10.0, plus a locally packed release and a real probe plugin from `out/Debug` and from a publish output. Expected files are derived from the checkout's assets file (12.28.0), not from the prototype. | Steps 6, 9, 10 |
| Refinements | `LoadedFiles` entries are candidates even when MEF skips them. Ordinary probing comes first, and detaching does not unload. The native support statement is narrowed. A stale non-empty directory is a failure. | Convention, Risks, step 9 |
