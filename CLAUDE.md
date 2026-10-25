# Cockpit — working notes for Claude Code

Single-user personal "operating system": Areas → Projects → Tasks, an Inbox, a weekly Review. One person (Michael) uses it from a phone several times a day and from a desktop once a week. The brief is `docs/cockpit-plan-prompt.md`; the design is `docs/PLAN.md`. Read both before changing scope.

## Non-negotiables (from the brief)
1. Single user, own server, one SQLite file is the data. **No telemetry, no analytics, no external API calls** from the app.
2. Modular: areas are the modules; custom fields per area come from `FieldDefinitions` + `PropertiesJson`, never from schema changes.
3. Everything editable inline where it is shown. No edit pages.
4. Views are derived from data. JSON export must stay lossless and re-importable; Markdown export must stay Obsidian-compatible.
5. Phone-first, works on desktop, dark mode, RTL-safe (`dir="auto"` on every text input/display).
6. Dependencies: mainstream, maintained, Microsoft/xUnit only unless agreed. **Name every new package and its reason in the PR.** No vendored JS/CSS, no code copied from random sources.
7. v2 items (finance module, notifications, MCP server, recurring tasks, calendar sync) are not built or scaffolded. Never: multi-user, native apps, time tracking, Gantt.

## Layout
```
src/Cockpit.Shared   DTOs, enums, view models, CockpitJson (shared JSON options) — compiled into server and client
src/Cockpit.Server   minimal API (Api/Endpoints.cs), EF Core (Data/), rules (Domain/), services (Services/), hosts the client
src/Cockpit.Client   Blazor WebAssembly PWA: Pages/, Components/ (Inline* editors, StatusFlow, Sheet, TaskRow…), Services/ApiClient
tests/Cockpit.Tests  xUnit: Support/TestHost wires real services on in-memory SQLite (runs real migrations); ApiTests boot the host
docs/                PLAN.md (decisions, schema, API, milestones, open questions), the original brief
```

## Run / test
- `dotnet run --project src/Cockpit.Server` → http://localhost:5080 (also reachable from the phone on the LAN via the PC's IP). Add `-- --seed` once to load the starter areas/projects. DB: `COCKPIT_DB` (default `src/Cockpit.Server/data/cockpit.db`, git-ignored).
- `dotnet watch --project src/Cockpit.Server` for hot reload.
- `dotnet test` (≈95 tests, ~5 s). Run it before every commit.
- `dotnet publish src/Cockpit.Server -c Release -o /tmp/cockpit-pub` and run `dotnet Cockpit.Server.dll` from there to check the Production build (that is what the Dockerfile ships). `docker compose up -d --build` to deploy.
- Schema change: `dotnet ef migrations add <Name> --project src/Cockpit.Server --output-dir Data/Migrations`, then commit the migration.
- UI smoke: Playwright + Chromium are available in the cloud container; a script lives in the session scratchpad, not in the repo.

## Conventions
- C# 13 / .NET 10, nullable on, file-scoped namespaces, primary constructors for services.
- Server rules live in `Domain/ProjectRules.cs` (pure) and are applied in `Services/ProjectService.cs`. Errors are `ApiError { code, message }` with stable codes (`wip_limit_exceeded`, `parked_requires_revisit`, …) mapped to 409/400.
- Writes are `PUT` with the full DTO. The server owns `Status`/`RevisitAt`/`CompletedAt` transitions, timestamps, and task→area consistency.
- Soft delete only (`DeletedAt` + global query filters). Export uses `IgnoreQueryFilters()`.
- Dates: `DateOnly` for due/deadline/revisit; `DateTime` UTC for timestamps (the DbContext forces UTC). Views take `?today=` from the client.
- The task enum is `TaskItemStatus` (not `TaskStatus`, which clashes with `System.Threading.Tasks`).
- Client: pages own their DTOs and call `ApiClient`; status changes go through `StatusFlow.ChangeStatusAsync` so the WIP/revisit prompts are consistent everywhere. After a mutation call `State.RefreshNavAsync()` for badge counts.
- CSS is one hand-written file (`wwwroot/css/app.css`) with theme tokens; no scoped `.razor.css`.
- Hosting: the server serves the client with `MapStaticAssets()` and `index.html` references the plain `_framework/blazor.webassembly.js` (`OverrideHtmlAssetPlaceholders` is off). Do not reintroduce `#[.{fingerprint}]` placeholders or `UseStaticFiles` for the client: the published app would not boot.
- Backups: `Services/BackupService.cs` (hosted service, SQLite online backup, settings `backup_hour` / `backup_retention_days`). Auth: `Api/AuthEndpoints.cs`, enabled only when `COCKPIT_PASSWORD` is set; the client shows `Components/Login.razor` on 401.
- Config comes from environment variables read through `builder.Configuration`: `COCKPIT_DB`, `COCKPIT_BACKUP_DIR`, `COCKPIT_PASSWORD`, `COCKPIT_SEED_ON_EMPTY`.
- Commits: small, one concern, imperative subject. One milestone per PR when possible.

## Scope creep
If a request (from anyone) adds something outside v1 of the brief, say so and point at `docs/cockpit-plan-prompt.md` before building it.
