# Cockpit

A single-user personal operating system: **Areas** (ongoing responsibilities) → **Projects** (finite, at most 3 active) → **Tasks** (next actions), with an **Inbox** for capture, a **Decide** queue for parked projects, area **health**, and a guided **weekly review**. Phone-first PWA, dark mode, Hebrew-safe, and your data stays in one SQLite file on your own machine.

Built with .NET 10 (minimal API + EF Core + SQLite) and Blazor WebAssembly. No telemetry, no external calls, no JavaScript framework. Design notes: `docs/PLAN.md`. Conventions for contributors (human or AI): `CLAUDE.md`.

## Run it locally

```bash
dotnet run --project src/Cockpit.Server -- --seed   # once: creates src/Cockpit.Server/data/cockpit.db with the starter areas/projects
dotnet run --project src/Cockpit.Server             # http://localhost:5080
```

On your phone (same Wi-Fi): open `http://<your-pc-ip>:5080` and use *Add to Home Screen*; it installs as an app.
Tests: `dotnet test` (≈ 95 tests, a few seconds).

## Deploy with Docker (one container)

```bash
git clone <this repo> cockpit && cd cockpit
docker compose up -d --build
```

- The SQLite file is `./data/cockpit.db` on the host; daily backups land in `./data/backups/`.
- The port is bound to `127.0.0.1:8080` on purpose. Reach it privately over Tailscale with `tailscale serve --bg 8080` (gives you HTTPS on your tailnet), or edit `docker-compose.yml` to expose it on the LAN.
- If the machine is reachable from the internet, set `COCKPIT_PASSWORD` in `docker-compose.yml` **and** put HTTPS in front (Tailscale serve, Caddy, nginx). The login is a single password with a 90-day cookie and a rate-limited login endpoint.
- First start seeds the areas/projects from the plan (`COCKPIT_SEED_ON_EMPTY=true`); it never touches a non-empty database.
- Update: `git pull && docker compose up -d --build`. Migrations run automatically at startup.

Environment variables: `COCKPIT_DB` (path of the database file), `COCKPIT_BACKUP_DIR` (default: `backups/` next to the database), `COCKPIT_PASSWORD` (empty = no login), `COCKPIT_SEED_ON_EMPTY`, `TZ`.

## Rules the app enforces

- **WIP limit** (default 3, changeable in Settings): activating one more project makes you park one first.
- **Parked means decided**: a parked project needs a revisit date; when it passes, the project shows up in *Decide*.
- **Every active project needs a next action**: flagged on the board, in *Areas* and in the review.
- **Inbox hygiene**: items older than 7 days are highlighted, and the weekly review warns before you continue.

## Your data

- The database file is the data. The app copies it once a day with SQLite's online backup API (safe while running) and keeps the newest ones (retention in Settings). *Settings → Back up now* for an immediate copy.
- *Settings → Export JSON*: lossless export (ids, timestamps, deleted rows, settings), re-importable with *merge* or *replace*.
- *Settings → Export Markdown*: a zip you can drop into an Obsidian vault — one note per area and project with YAML frontmatter and `[[wikilinks]]`, task checklists, your notes, the inbox and every weekly review.

## Layout

```
src/Cockpit.Shared   DTOs, enums, view models shared by server and client
src/Cockpit.Server   minimal API, EF Core + SQLite, rules, views, export, backups, auth; hosts the client
src/Cockpit.Client   Blazor WebAssembly PWA (pages, inline-edit components, one CSS file, ~20 lines of JS)
tests/Cockpit.Tests  xUnit: rules, views, export round trip, review wizard, backups, auth, HTTP API
docs/                PLAN.md (decisions, schema, API, milestones, open questions) and the original brief
```

## Dependencies

Microsoft packages only (EF Core SQLite + Design, ASP.NET Core Components WebAssembly + Server, Mvc.Testing) plus xUnit. Listed with reasons in `docs/PLAN.md` §9.
