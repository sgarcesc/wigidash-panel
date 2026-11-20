# Security policy

## Supported versions

Only the [latest release](https://github.com/sgarcesc/wigidash-panel/releases/latest) gets
security fixes. Fixes ship as a new release; update by running the newer installer.

## Reporting a vulnerability

Please don't open a public issue. Report it privately through GitHub instead:
**[Security → Report a vulnerability](https://github.com/sgarcesc/wigidash-panel/security/advisories/new)**.

Include what you found, how to reproduce it (a layout or skin file that triggers it, if relevant),
and the release or commit you tested. Once a fix is released, the advisory is published with credit
to you unless you'd rather stay anonymous.

## Scope

In scope:

- The widget DLL, including how it parses JSON layouts and AIDA64 `.gslcd`/`.sensorpanel` skins
  (these files often come from third parties, so a crafted file that crashes WigiDash Manager or
  runs code is a vulnerability).
- The installer, `install.ps1`, and the release workflow that builds them.

Out of scope: WigiDash Manager, HWiNFO and AIDA64 themselves (report those to their vendors), and
third-party skins.

## Verifying downloads

Release files carry signed build provenance. See
[Install → From a release](README.md#from-a-release) for the `gh attestation verify` command.
