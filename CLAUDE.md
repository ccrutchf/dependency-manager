# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

Targets .NET 10 (`net10.0`). Solution file: `DependencyManager.sln`. Assembly name is `depend` (not `DependencyManager`).

- Build: `dotnet build` (Debug) or `dotnet build -c Release`. Single-file self-contained settings are scoped to `dotnet publish` (`_IsPublishing=true`), so plain builds don't need a RID.
- Run CLI against the example config: `dotnet run --project src/DependencyManager -- plan --config examples/packages.yaml`
- Run all tests: `dotnet test`
- Run one test class: `dotnet test --filter "FullyQualifiedName~PlannerTests"`
- Run one test: `dotnet test --filter "FullyQualifiedName~PlannerTests.Topo_sort_places_dependencies_first"`
- Publish a release binary (mirrors `.github/workflows/release.yml`): `dotnet publish src/DependencyManager -c Release -r linux-x64 -o out/linux-x64` (supported RIDs: `linux-x64`, `linux-arm64`)

`TreatWarningsAsErrors=true` — warnings will break the build.

**Work test-first (TDD).** Add or update a test that captures the new behavior, run it and watch it fail, then write the minimum code to make it pass. This applies to everything — config keys, planner/runner logic, root rules, and the pure logic behind managers. See *Tests* below.

## Architecture

The pipeline has three stages: **load → plan → run**. Each stage has a single entry point and is unit-testable without the others.

**Config (`src/DependencyManager/Config/`)** — YAML is a top-level map of named *blocks*. Each block has filter keys (`platform`, `architecture`, `version`), optional `ppas`, optional `requires` (external prerequisites — see Planner), and per-manager package maps (`apt`, `snap`, `flatpak`, `deb`, `pip`, `pipx`, `cargo`, `nvm`, `script`, `vscode`, and the browser-extension keys `firefox`, `zen`, `chrome`, `chromium`, `brave`). `BlockYamlConverter` is a hand-rolled `IYamlTypeConverter` rather than plain POCO deserialization because it needs to distinguish filter keys from provider keys in the same flat map and emit a warning for unknown keys. If you add a new top-level block key, update both `FilterKeys`/`ProviderKeys` sets there and the `Block` record in `ConfigSchema.cs`.

**Planner (`Runner/Planner.cs`)** — Filters blocks via `BlockFilter.Matches` (with `"all"` as the wildcard and version matching via `StartsWith`), flattens all matching package sections into a `(ManagerKind, Id)` dictionary (so later blocks overwrite earlier ones on the same key), dedupes PPAs, then topologically sorts by `Spec.Dependencies`. Dependency lookup is keyed by `Spec.Name ?? Id`, so a package can override its lookup name; unknown dependency names are silently skipped. Cycles throw. Block-level `requires:` entries are resolved via `Util/PathLookup` (binary on `PATH`, or absolute path) and surfaced as `ResolvedRequirement` records on the plan — they're intended for *external* prerequisites depend does not install (e.g. asserting `code` is present before applying VSCode extensions when VSCode itself is installed via Nix). `install` aborts before running if any are unsatisfied; `plan` and `test` report them.

**Runner (`Runner/Runner.cs` + `Managers/`)** — `Runner` iterates the sorted plan, finds a matching available `IPackageManager`, bootstraps it on first use (apt: adds PPAs then `apt-get update`; flatpak: adds flathub remote; snap: no-op), then `IsInstalledAsync` → `InstallAsync`. Managers shell out through `Util/ProcessRunner.cs` and are selected by `Kind` + `IsAvailable()` (binary presence checks like `/usr/bin/apt-get`). Add a new provider by implementing `IPackageManager`, adding a `ManagerKind` enum value, wiring it into `InstallCommand.BuildManagers` (and `UpdateCommand`/`ListCommand`), and extending `BlockYamlConverter`'s provider keys + the `Block` record.

**Browser extensions (`Managers/BrowserExtensionManager.cs` + `BrowserCatalog.cs` + `BrowserPolicy.cs`)** — Browsers ship no extension-install CLI (unlike VS Code), so depend installs extensions by writing enterprise *policy files*. There are five managers (`firefox`, `zen`, `chrome`, `chromium`, `brave`) — each its own `ManagerKind` so the same extension id can target multiple browsers without colliding on the `(ManagerKind, Id)` plan key. Two policy *families* (`BrowserPolicyFamily` in `BrowserPolicy.cs`): Firefox-family (Firefox, Zen) merge a single `policies.json` with `policies.ExtensionSettings` (key = addon id like `uBlock0@raymondhill.net`; url is the `.xpi`, built from a `source:` AMO slug if not given as `url:`); Chromium-family (Chrome, Chromium, Brave) write one `depend-<id>.json` per extension into a `policies/managed/` dir (key = 32-char Web Store id; `update_url` defaults to the Chrome Web Store). `mode:` maps to `installation_mode` (default `normal_installed`; also `force`, `allowed`, `blocked`). `BrowserCatalog` is the "known paths" table: per browser it lists detection binaries and the native/snap/flatpak policy targets (e.g. `/etc/firefox/policies`, `<install>/distribution`, `/var/snap/<x>/current`, and the `…systemconfig`/`…Extension.system-policies` flatpak extension points). For Firefox-family the actual `<install>/distribution` is also derived at runtime by resolving the binary through symlinks (covers Zen's varied install dirs). `BrowserPolicy`/`BrowserCatalog` keep the JSON, mode/url, and path-building logic pure and unit-tested; the manager does the filesystem detection and writes. `IsAvailable()` is true if the browser is detected via any variant; a *detected* browser with no writable policy location, or a missing browser, surfaces as a failure/skip the same way other unavailable managers do.

**Root requirement** — `Util/RootCheck.PlanRequiresSudo` returns true if the plan has any PPAs, any apt/snap/deb package, any browser-extension package (`RootCheck.IsBrowserExtension`), or any package with an explicit `user_scope: false`. The depend process always runs as the normal user (`install`/`update` refuse to run as root) and escalates *individual commands* through `Util/Sudo` (which prepends `sudo` when not already root); euid is checked via `libc.geteuid` P/Invoke. Browser policy writes follow this model: per-user flatpak files are written directly, system paths via `sudo install -D -m 0644`. Browser kinds prime sudo conservatively because native/snap/system installs need root — a pure per-user flatpak setup is the only case that wouldn't. Flatpak/pip/pipx default to `--user` scope and only escalate to sudo when `user_scope: false` is set. `plan` and `test` never need root.

## CLI entry

`Program.cs` uses `System.CommandLine` 2.0 beta 4. Subcommands: `plan`, `install` (supports `--fail-fast`), `test`, `list`, `update`. `plan`/`install`/`test` accept `--config`/`-c` (default `packages.yaml`); `list` and `update` take no config. Commands live in `Commands/` as static classes with `Run`/`RunAsync`.

`update` runs `UpdateAllAsync` on every available `IPackageManager`. On NixOS (detected by `nixos-rebuild` on `PATH`), it also runs `sudo nixos-rebuild switch --upgrade` *first* so subsequent user-scope updates execute against the new system generation. This system-wide updater is intentionally inline in `UpdateCommand` rather than another `IPackageManager` — that interface is per-package and nixos-rebuild has no Install/IsInstalled semantics; treat future per-system updaters (e.g. `brew upgrade`, `winget upgrade --all`) the same way until there are enough of them to warrant an abstraction.

## Tests

**Development here is test-driven (see Commands): the test comes first.** Because a behavior change starts from a failing test, structure code so the behavior is reachable from a test without a real package manager or filesystem. The pattern: keep the testable core in pure helpers and leave only a thin shell-out/filesystem layer untested — e.g. `BrowserPolicy` (JSON/mode/url) and `BrowserCatalog` (path-building) are fully unit-tested, so `BrowserExtensionManager`'s untestable surface (detection, `sudo install`, file writes) stays small. Do the same for new providers.

xUnit + Shouldly. Tests construct `ConfigFile`/`Block` records directly rather than parsing YAML, except `ConfigLoaderTests` which exercises the YAML pipeline end-to-end. There are no integration tests that hit real package managers — the thin `IPackageManager` filesystem/process layer is verified manually.
