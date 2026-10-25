# Cockpit

A single-user personal operating system: **Areas** (ongoing responsibilities) → **Projects** (finite, at most 3 active) → **Tasks** (next actions), with an **Inbox** for capture, a **Decide** queue for parked projects, area **health**, and a guided **weekly review**. Phone-first PWA, dark mode, Hebrew-safe, your data in one SQLite file.

Built with .NET 10 (minimal API + EF Core + SQLite) and Blazor WebAssembly. No telemetry, no external calls, no JavaScript framework. See `docs/PLAN.md` for the design and `CLAUDE.md` for conventions.

## Run it

```bash
dotnet run --project src/Cockpit.Server -- --seed   # first time: creates ./src/Cockpit.Server/data/cockpit.db with starter areas/projects
dotnet run --project src/Cockpit.Server             # http://localhost:5080
```

On your phone (same Wi-Fi): open `http://<your-pc-ip>:5080`, then "Add to Home Screen" to install it as an app.

Tests: `dotnet test`.

## Rules the app enforces

- **WIP limit** (default 3, Settings): activating one more project makes you park one first.
- **Parked means decided**: a parked project needs a revisit date; when it passes, the project shows up in *Decide*.
- **Every active project needs a next action**: flagged on the board and in *Areas*.
- **Inbox hygiene**: items older than 7 days are highlighted (and warned about in the weekly review).

## Your data

- The database file is the data. Back it up by copying it (the app's own daily backup arrives in milestone 6).
- *Settings → Export JSON* gives a lossless export (ids, timestamps, deleted rows). *Import* merges by id or replaces everything.
- Markdown export for Obsidian: milestone 5.

## Status

Milestones 1–2 of 6 are implemented (see `docs/PLAN.md` §5). Coming: custom field definitions UI, weekly review wizard, Markdown export, Docker + backups + optional login.
