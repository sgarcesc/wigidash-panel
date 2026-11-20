# Review instructions

## What Important means here

The widget runs inside WigiDash Manager's process. A crash or hang in the widget takes down the
Manager and blanks the device. Reserve Important for:

- An exception that can escape a thread the widget doesn't own: timer callbacks,
  `ThreadPool` work, `SensorUpdated`, `FileSystemWatcher` events and settings UI handlers. These
  paths must catch and `Log(...)`.
- Deadlock or blocking risk: taking a lock or doing slow work inside `SensorHub.OnSensorUpdated`,
  binding work on the UI thread, or holding `renderGate` while calling into the Manager. See the
  threading remarks in `SensorHub` and `PanelInstance`.
- A GDI+ object (`Bitmap`, `Graphics`, `Font`, `Brush`, `Pen`, `PrivateFontCollection`) created
  per tick or per reload and never disposed. This leaks handles until the Manager dies.
- Disposing a frame `Bitmap` the Manager may still be reading. `PanelInstance` double-buffers on
  purpose, so a frame is disposed two ticks after it's handed over, never right away.
- A layout or AIDA skin that can crash the widget. These files are user-supplied or third-party.
  Bad input must become a layout error or a per-item warning, not an exception that escapes.
- A sensor binding change that leaves a shipped layout unbound on any machine covered by
  `tests/WigiPanel.Tests/Fixtures`.
- A WigiDashWidgetFramework API used in `src/` that's missing from
  `stubs/WigiDashWidgetFramework/Api.cs`, which breaks the CI build. Also any change that would
  ship the stub DLL or copy the real one next to the widget.
- A change to the assembly name or the widget GUID.

Style, naming and refactoring suggestions are Nit at most.

## Cap the nits

Report at most five Nits per review.

## Do not report

- Diffs in the generated native skins in `layouts/` (every `.json` except `default.json` and
  `aida-sensors.json`). Review `designs/make-designs.ps1` instead. Do report a PR that changes a
  generated skin without a matching change to the script.
- Rendered images (`designs/*.png`, `docs/*.png`), sensor dump fixtures
  (`tests/WigiPanel.Tests/Fixtures/*.txt`) and `package-lock.json`.
- Missing unit tests for `Widget/`. It depends on the live Manager and can't be unit-tested.
- Defensive `catch { }` around font loading, snapshot writes, or other best-effort operations
  that are optional by design.
- Use of older C# or .NET APIs that `net472` requires.

## Always check

- Behaviour claims about WigiDash Manager or HWiNFO need a `file:line` citation from this repo
  (the stub, a fixture, or existing handling). Don't infer them from names.
- A new sensor regex in a layout or in `aida-sensors.json` matches the naming in the fixtures, both
  the current `dGPU`/`iGPU` source names and the legacy `GPU` ones.
- A bug fix in `Layout/` or `Rendering/` comes with a test that fails without the fix. For a
  sensor-binding bug, that test is a new fixture.
- The PR title follows Conventional Commits, and its type matches the change. `feat:` and `fix:`
  cut a release. `docs:`, `ci:` and `chore:` don't.
- No AIDA64 skin files (`.gslcd`, `.sensorpanel`) or their embedded artwork are committed.
