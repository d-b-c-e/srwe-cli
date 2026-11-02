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

<!-- BEGIN project-file-location -->
## Project files and temporary work

Keep project-related research, evidence, scripts, packages, and other working files inside the relevant project folder whenever practical. Keep disposable or machine-specific outputs git-ignored when they do not belong in version control; never ignore files that should be committed merely to hide them. When a project folder is unsuitable, use `E:\Source\temp\<project>` instead of creating scattered drive-root folders. Record the location of material needed by later work and update references when relocating it.

Clean up temporary files and disposable copies promptly once they are no longer useful. Preserve active work, user data, settings, saves, required evidence, backups, rollback artifacts, and worktrees until their retention or replacement is established. Follow existing project, rig, and data-loss safeguards; do not remove files merely because they are old or untracked.
<!-- END project-file-location -->
