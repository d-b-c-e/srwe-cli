# AGENTS.md

## Repository Purpose

Provide a headless Windows CLI fork of SRWE for reliable runtime window placement, particularly
for windowed games spanning multiple physical displays.

## Repository Structure

- `SRWE.Cli/` — supported .NET 10 Windows CLI.
- `SRWE.Cli.SmokeTests/` — non-interactive window-placement and profile smoke tests.
- `SRWE/` — upstream legacy GUI source; preserve its history and attribution.
- `Profiles/` — upstream profile examples.

## Conventions

- Keep native Win32 operations isolated in `NativeWindow.cs`; report the resulting client rectangle.
- Do not alter display topology or another process without an explicit CLI target and apply command.
- Preserve compatibility with SRWE XML profiles and the MIT copyright notice.
- Build and run CLI smoke tests before committing window-control changes.
