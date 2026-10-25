# Cockpit — plan and design decisions

This is the plan the implementation follows. The brief is in `docs/cockpit-plan-prompt.md`.
Where the brief left a bracket to fill, the choice made here is marked **[assumption]** and listed again under *Open questions*.

## 1. Stack decision

**Chosen: .NET 10 (LTS) minimal API + EF Core 10 + SQLite on the server; Blazor WebAssembly PWA as the client, hosted by the same ASP.NET Core process; xUnit for tests. No npm, no JavaScript framework; one container.**

Why this fits the brief:

- *2 minutes from the phone, several times a day.* A WebAssembly PWA is installed on the home screen and re-opens instantly from cache. It needs no persistent connection: a Blazor Server circuit drops every time the phone sleeps and shows a "reconnecting" overlay on wake, which is exactly the moment you want to tick a task.
- *Single-person maintenance, .NET background.* Server, client and tests are one language, one solution, one `dotnet build`. DTOs and enums live in `Cockpit.Shared` and are compiled into both sides, so there is no API/UI drift.
- *v2 wants an MCP server.* Because the UI only talks to the JSON API, the API is already the integration surface; the MCP server will be another client of the same endpoints.
- *Trusted dependencies only.* Everything is a Microsoft package from nuget.org plus xUnit (see §9). No vendored CSS/JS libraries; the stylesheet and the ~20 lines of JavaScript are our own.

Rejected alternatives:

1. **Blazor Server.** Simplest to write, but every interaction is a round trip over SignalR, the circuit dies when the phone backgrounds the browser, and there is no offline app shell, so it cannot be a real PWA. Wrong fit for a phone-first tool used in short bursts.
2. **Svelte/React/Vue SPA + .NET API.** Best raw UI ergonomics, but it adds a second toolchain (Node, Vite) and an npm dependency tree that conflicts with "list every dependency you add", and loses the shared C# models. For one person it is more surface to keep working for a UI of eight screens.

Also considered: Razor Pages + htmx (rejected: inline-edit-everywhere, a kanban board and a multi-step wizard need client state; htmx would end up re-implementing a SPA in attributes).

Known cost of the choice: the first load of the WebAssembly runtime is a few MB (cached afterwards by the service worker); and the server must run .NET, which the Docker image handles.

## 2. Schema

One SQLite file (`COCKPIT_DB`, default `./data/cockpit.db`), WAL mode, foreign keys on. Ids are GUIDs so exports/imports preserve identity. All timestamps are UTC; calendar fields (`due`, `deadline`, `revisit_at`) are `DateOnly` because a planner works in days, not instants.

| Table | Columns (besides `Id`, `CreatedAt`, `UpdatedAt`, `DeletedAt`) | Indexes |
|---|---|---|
| Areas | Name, Kind, Color, ReviewCadence, SortOrder, Archived, PropertiesJson | (Archived, SortOrder) |
| Projects | AreaId→Areas, Name, Status, Goal, NextMilestone, Deadline, RevisitAt, Priority, Notes, PropertiesJson, SortOrder, CompletedAt | (AreaId, Status), (Status, RevisitAt), Deadline |
| Tasks | AreaId→Areas, ProjectId→Projects (nullable), Title, Status, Due, Effort, Tags (JSON array), SortOrder, PropertiesJson, CompletedAt | (AreaId, Status), (ProjectId, Status), (Status, Due) |
| InboxItems | Text, ProcessedAt, ConvertedTo, ConvertedId | ProcessedAt |
| Reviews | Status, Step, StartedAt, FinishedAt, Notes, SnapshotJson, DecisionsJson | StartedAt, Status |
| FieldDefinitions | AreaId→Areas, AppliesTo (project/task), Name, Key, Type, Options (JSON), SortOrder, Archived | unique (AreaId, AppliesTo, Key) |
| Settings | Key (PK), Value | — |

Design points:

- **Custom fields without migrations.** `FieldDefinitions` describes the fields an area adds to its projects or tasks. Values live in the row's `PropertiesJson` object keyed by the definition's `Key` (`"budget": 12000`). Adding, renaming or removing a field is a data change. The key is derived from the name once and never changes, so renaming a field keeps old values readable.
- **Enums are stored as strings** (`Active`, `Parked`) so the `.db` is readable with `sqlite3`, and adding a value (a new area kind, a new field type) needs no migration.
- **Soft deletes everywhere.** `DeletedAt` + EF global query filters; nothing the UI does is a hard delete. Deleting an area or a project cascades the soft delete to its children. Exports include deleted rows.
- **Tasks always carry their area**, even when they belong to a project (the server enforces `task.AreaId == project.AreaId` and follows the project if it moves). This keeps "group by area" a plain query.
- **Settings** is a key/value table (`wip_limit`, `inbox_stale_days`, `backup_hour`, `backup_retention_days`), so new settings never need a migration either.
- **Migration strategy.** EF Core migrations are checked in (`src/Cockpit.Server/Data/Migrations`) and applied at startup (`Database.Migrate()`), which is fine for a single-user app. A new migration is only needed when a *column* changes: `dotnet ef migrations add <Name> --project src/Cockpit.Server`. Tests run the real migrations against an in-memory SQLite, so a forgotten migration fails the build.

## 3. API surface

All JSON, camelCase, enums as snake_case strings, under `/api`. Writes use `PUT` with the full object (the inline editor always has it; it sidesteps "null vs. not provided"). Errors are `{ code, message, ... }` with a stable `code` the UI switches on.

```
GET  /api/health
GET  /api/settings                       PUT /api/settings
GET  /api/areas[?includeArchived]        POST /api/areas   GET|PUT|DELETE /api/areas/{id}   POST /api/areas/reorder
GET  /api/fields[?areaId&appliesTo]      POST /api/fields  PUT|DELETE /api/fields/{id}
GET  /api/projects[?areaId&status]       POST /api/projects  GET|PUT|DELETE /api/projects/{id}  POST /api/projects/reorder
GET  /api/tasks[?areaId&projectId&includeClosed]  POST /api/tasks  GET|PUT|DELETE /api/tasks/{id}  POST /api/tasks/reorder
GET  /api/inbox[?includeProcessed]       POST /api/inbox   PUT|DELETE /api/inbox/{id}
POST /api/inbox/{id}/process  { convertTo: task|project|note|discarded, areaId?, projectId?, title?, due? }
POST /api/inbox/{id}/reopen
GET  /api/views/today|week|board|decide|areas|nav   ?today=yyyy-MM-dd   (the phone sends its own calendar date)
GET  /api/export/json  (download)   GET /api/export/document   POST /api/import/json?mode=merge|replace
GET  /api/export/markdown (zip)                                   — milestone 5
GET  /api/reviews/current  POST /api/reviews  POST /api/reviews/{id}/step|decision|finish|abandon — milestone 4
```

Rule responses: activating a project over the WIP limit → `409 { code: "wip_limit_exceeded", limit, activeProjects: [...] }`; parking without a date → `400 { code: "parked_requires_revisit" }`.

## 4. Screens

Layout: on phones a bottom bar (Today · Week · Board · Inbox · More) with badge counts, and a "More" sheet for Areas, Decide, Weekly review, Settings, Theme. At ≥ 900 px a left sidebar with all eight. Dark mode follows the OS and can be toggled (stored in `localStorage`). Every text field and display has `dir="auto"`, so Hebrew renders right-to-left inside a left-to-right UI.

**Inline editing on touch.** There is no edit mode. Each value is one of five small components:
`InlineText` (tap the text → it becomes an input with the keyboard open; Enter or tapping away saves, Escape cancels; multiline for notes with Ctrl/⌘+Enter),
`InlineSelect` / `InlineChoice` (a chip that *is* a native `<select>`, so one tap opens the OS picker),
`InlineDate` (a chip over a native `<input type=date>` with an × to clear),
`InlineCheckbox`, `InlineNumber`, `InlineTags` (comma separated → chips).
Each change is saved immediately (optimistic), and on failure the value snaps back and a toast explains why. Targets are ≥ 44 px.

| Screen | What is on it |
|---|---|
| **Today** `/` | Pinned capture box → Inbox. Tasks grouped by area, each tagged OVERDUE / TODAY / DOING. Footer links to Inbox and Decide counts. |
| **Week** `/week` | Dated tasks for the next 7 days grouped by day, then the active projects as cards with milestone/deadline. |
| **Projects board** `/board` | WIP pill `n/limit` always in the header, area filter, "+ Project". Desktop: three columns (Active · Parked · Done, dropped foldable) with HTML5 drag between columns. Phone: a tab per column, and every card's ⋯ menu offers "Move to …" (drag is unreliable on touch). Status changes run through `StatusFlow`, which owns the two rule prompts: *pick a revisit date* and *WIP reached — park one of these first*. |
| **Project** `/projects/{id}` | Area (changeable), name, status, priority, deadline, revisit (when parked), goal, next milestone, custom fields, tasks (add box, done toggle, each task fully inline), notes (markdown text), timestamps, ⋯ → delete. |
| **Decide** `/decide` | Parked projects whose revisit passed, oldest first, with Activate / Park again (asks for a new date) / Done / Drop. |
| **Areas** `/areas` | One card per area: active / parked / open tasks, last touched, nearest deadline, health flags (no next action, overdue, revisit due, deadline soon/passed). `/areas/{id}` edits the area inline and lists its projects and loose tasks. |
| **Inbox** `/inbox` | Capture box; each item: editable text, age (stale items highlighted), → Task / → Project / → Note / Discard. Task and Note pick a project (or an area for a loose task); Project picks an area and a revisit date (new projects start parked). Undo via toast. |
| **Weekly review** `/review` | Milestone 4: five steps (inbox → walk active projects → decide → choose next week's ≤ WIP → notes & finish) writing a Review record. |
| **Settings** `/settings` | WIP limit, stale-inbox days, areas (inline CRUD, archive), field definitions per area (milestone 3), export JSON / Markdown, import JSON (merge or replace). |

## 5. Milestones

Each is one evening. The app is usable on the phone after milestone 1.

| # | Scope | State |
|---|---|---|
| 1 | Solution, schema + migration, API, rules, JSON export/import, seed, Board + Project + Tasks + Inbox + Settings UI, PWA shell, tests for WIP / parked / round trip | done (commit M1; also first versions of Today, Week, Decide, Areas) |
| 2 | Today + Week polish, capture, inline-edit refinements, PWA icons/manifest, dark mode, RTL | done inside M1 |
| 3 | Areas health + area page, Decide, FieldDefinitions UI and generic custom-field rendering | done (commit M3) |
| 4 | Weekly review wizard + Review record + state-machine tests | done (commit M4) |
| 5 | Markdown (Obsidian) export, import UX, settings completion | done (commit M5) |
| 6 | Dockerfile + Compose, in-app daily SQLite backup, optional password gate, README/CLAUDE.md | done (commit M6) |

## 6. Test plan → rules

| Rule | Tests |
|---|---|
| WIP limit (max N active, configurable; activating a 4th is refused and names the active ones) | `WipRuleTests` (pure rule table + service: 4th rejected with 409 listing actives, parked→active at limit rejected, park-one-then-activate works, editing an active project doesn't count itself, done/dropped don't count, limit from settings), `ApiTests.Wip_violation_is_a_409…` |
| Parked means decided (revisit required; past revisit ⇒ Decide, oldest first) | `ParkedRevisitTests` (pure rule table, create/update rejected with 400, Decide ordering incl. "today", future dates excluded, inbox→project starts parked with a date, completed_at stamping), `ApiTests.Parked_without_revisit…` |
| Every active project needs a next action | `HealthRuleTests.Active_project_with_no_open_task_is_flagged…` |
| Inbox hygiene (7-day soft warning) | `HealthRuleTests.Stale_inbox_counts…` |
| Export → import round trip (lossless, deleted rows included, merge upserts, bad files rejected atomically) | `ExportRoundTripTests`, `ApiTests.Export_is_a_json_download…` |
| Review wizard state machine (forward one / back any, finish only at the end and within WIP, one in-progress review, known decisions, snapshots) | `ReviewWizardTests` |
| Backups (consistent single-file copy, prune keeps newest three, once per day after the hour) | `BackupTests` |
| Password gate (401 until login, cookie, logout, rate limit, open when unset, app shell public) | `AuthTests`, `NoPasswordTests` |

Tests use the real services on an in-memory SQLite database that runs the checked-in migrations; `ApiTests` boot the real host with `WebApplicationFactory`. Run: `dotnet test`.

## 7. Dev / deploy

- Run locally: `dotnet run --project src/Cockpit.Server` → http://localhost:5080 (binds `0.0.0.0`, so the phone on the same Wi-Fi can open `http://<pc-ip>:5080`). First time: `dotnet run --project src/Cockpit.Server -- --seed`.
- Hot reload: `dotnet watch --project src/Cockpit.Server`.
- Tests: `dotnet test`.
- Deploy: `docker compose up -d --build`; one container (multi-stage Dockerfile on Microsoft's sdk/aspnet images), `./data` mounted at `/data`, `COCKPIT_DB=/data/cockpit.db`, `COCKPIT_SEED_ON_EMPTY=true` for the first start. The port binds to localhost; reach it over Tailscale (`tailscale serve --bg 8080`) or open it on the LAN. `COCKPIT_PASSWORD` enables the single-user login (cookie, 90 days sliding, login rate-limited 5/min/IP) for anything exposed beyond a private network — always behind HTTPS.
- Backup: `BackupService` inside the app runs SQLite's online backup API once a day after `backup_hour` (server local time), writes `cockpit-yyyyMMdd-HHmmss.db` single-file copies (WAL converted to rollback journal) into `COCKPIT_BACKUP_DIR`, prunes beyond `backup_retention_days` but always keeps the newest three. "Back up now" in Settings. Plus `Export JSON` / `Export Markdown` at any time.
- Hosting detail worth knowing: the server serves the client through `MapStaticAssets()` (the static web assets endpoint manifest), which gives fingerprinted framework files immutable caching and serves the precompressed `.br`/`.gz` variants; `index.html` references the plain `_framework/blazor.webassembly.js`, which the manifest aliases. Serving the published `wwwroot` with plain `UseStaticFiles` would leave the SDK's `#[.{fingerprint}]` placeholder unprocessed and the app would not boot — that is why `OverrideHtmlAssetPlaceholders` is off.

## 8. Risks, assumptions, open questions

Risks
- Blazor WebAssembly first load on a slow phone connection (~3–4 MB once; cached afterwards). Mitigation: served from the same host, service worker caches the shell.
- `PUT` full-object saves mean last-write-wins between two open tabs. Acceptable for one user; the UI reloads the entity after each save.
- HTML5 drag-and-drop is desktop-only; the phone uses the card menu.

Assumptions **[assumption]**
- "This week" = today through today + 6 (no week-start ambiguity).
- The phone's local calendar date defines "today"; the client sends it to every view.
- New projects created from the inbox start *parked* with a revisit date, because activating is the deliberate step the WIP rule guards.
- Seed: active = AI discovery scanner MVP, Vektralog, Holon apartment search; everything else parked until 2026-12-01; *Day job* has no projects yet.

Open questions
1. Confirm the three initially active projects and the Day job projects (Seeder.cs, or just change them in the UI).
2. Should archived areas' tasks disappear from Today immediately (current behaviour) or stay until done?
3. Priority is low / normal / high — enough, or do you want a numeric rank?
4. Password gate: implemented and off by default (`COCKPIT_PASSWORD` empty). Keep it off on a Tailscale-only box?
5. Markdown notes: edited as plain text — do you want a rendered preview (would add the Markdig package)?
6. Backups default to 03:00 server time, 30 days retention; the container's `TZ` is set to Asia/Jerusalem in Compose — correct?

## 9. Dependencies and why

| Package | Where | Why |
|---|---|---|
| Microsoft.EntityFrameworkCore.Sqlite 10 | Server | ORM + SQLite provider (migrations, query filters, JSON collections) |
| Microsoft.EntityFrameworkCore.Design 10 | Server (design-time) | `dotnet ef migrations` |
| Microsoft.AspNetCore.Components.WebAssembly.Server 10 | Server | serves the Blazor WebAssembly client from the API host |
| Microsoft.AspNetCore.Components.WebAssembly 10 (+ DevServer, dev only) | Client | Blazor WebAssembly runtime |
| dotnet-ef 10 (local tool) | repo | migration CLI |
| xunit 2.9, xunit.runner.visualstudio, Microsoft.NET.Test.Sdk | Tests | the standard .NET test stack |
| Microsoft.AspNetCore.Mvc.Testing 10 | Tests | boot the real host in-process for HTTP tests |

Nothing else. No analytics, no telemetry, no external calls at runtime.
