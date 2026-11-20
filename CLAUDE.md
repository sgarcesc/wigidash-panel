# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

# Commands

- Test: `dotnet test tests\WigiPanel.Tests` (CI adds `-c Release`)
- Single test: `dotnet test tests\WigiPanel.Tests --filter "FullyQualifiedName~Integrated_only"`
- Check a layout visually without the device: `dotnet build tools\Preview -c Release`, then
  `tools\Preview\bin\Release\wigipanel-preview.exe layouts\<name>.json out.png [width height]`
  (renders each binding's `demo` value)
- No linter. `npm` exists only for semantic-release.
- `scripts\install.ps1` closes and restarts WigiDash Manager. Don't run it unless asked.

# Conventions

- Native skin JSON in `layouts/` (everything except `default.json` and `aida-sensors.json`) is
  generated. Edit `designs/make-designs.ps1` and re-run it with `pwsh designs\make-designs.ps1`.
  Don't hand-edit the generated files.
- When HWiNFO renames sensors, add a fixture to `tests/WigiPanel.Tests/Fixtures`. A fixture is a
  `sensors.txt` saved with the widget's **Sensor list** button.
- Commit messages are Conventional Commits, and semantic-release cuts a release on every push to
  `main`: `fix:` gives a patch, `feat:` a minor release and `BREAKING CHANGE:` a major one.
  `docs:`, `ci:` and `chore:` don't release. Tags are plain `X.Y.Z`, with no `v`.
- PRs are squash-merged, so PR titles must also follow Conventional Commits. The title becomes the
  commit on `main`.
- Don't commit AIDA64 skins (`.gslcd`/`.sensorpanel`). They're third-party work.

# Gotchas

- The assembly name is the widget GUID (`56507051-FDDD-447B-B59B-81D78C7CD961`), because WigiDash
  Manager loads `Widgets\<GUID>\<GUID>.dll`. Don't rename it.
- When WigiDash Manager isn't installed (CI, forks), the build compiles against
  `stubs/WigiDashWidgetFramework` instead of the real framework DLL. Any newly used framework API
  must also be added to `stubs/WigiDashWidgetFramework/Api.cs`, or CI fails. The stub is
  compile-only and must never ship.
- `src/WigiPanel/Rendering/` must not reference WigiDash types. The Preview tool and the tests use
  it without the Manager.
- `Widget/SensorHub.cs` runs on the Manager's sensor thread, and the Manager may hold locks there.
  Follow the threading rules in its class remarks: no locks in `OnSensorUpdated`, and no binding
  work on the UI thread.
- Everything targets `net472` with WPF/GDI+, so it builds and runs on Windows only.
