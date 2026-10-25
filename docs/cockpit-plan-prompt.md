# Cockpit — planning prompt for Claude Code

> Open Claude Code in a new, empty repo, switch to plan mode, and paste everything below this line as your first message (or save this file in the repo and say "read cockpit-plan-prompt.md and follow it"). Fill the `[brackets]` first. "Cockpit" is a working name — change it.

---

## Who I am and why this exists

I'm Michael, a solo developer (7+ years, mostly .NET/C#, also Java and Android). I run a small dev studio (IvnoWorks) with several products, I'm trying to build a startup, I do side PPC/marketing work, I have a day job, and I have home and household-finance responsibilities. I have too many parallel things and no single place to see and manage them.

I'm building Cockpit: a personal, single-user "operating system" for all of it. Usage pattern: 2 minutes several times a day from my phone (capture, tick tasks, check Today), 20 minutes once a week from my desktop (weekly review). It's for me only. My data stays with me.

## Non-negotiables

1. Single user, my data, my server. No third-party SaaS holds the data. No telemetry, no analytics, no external API calls in v1.
2. Modular: Areas are the modules. I can add/remove areas and add custom fields per area from the UI, without code changes or migrations.
3. Fully editable: every field is editable inline where it's displayed. No "edit mode" page-hopping.
4. Data-driven: all views are derived from the data; nothing is hardcoded. Export everything to JSON (lossless, re-importable) and to Markdown (one file per project/area, YAML frontmatter, Obsidian-compatible) at any time.
5. Phone-first UI that also works well on desktop. Dark mode. RTL-safe (I'll type Hebrew in fields sometimes).
6. Trusted dependencies only: mainstream, maintained packages. List every dependency you add and why. No obscure packages, no code copied from random sources.

## Core model (adjust names, keep the semantics)

- **Area** — ongoing responsibility, never "done". `name`, `kind` (work | business | startup | home | finance | health | other), `color`, `review_cadence` (weekly | monthly), `sort_order`, `archived`, `properties` (JSON, custom fields).
- **Project** — finite, has an end. Belongs to one Area. `name`, `status` (active | parked | done | dropped), `goal`, `next_milestone`, `deadline` (nullable), `revisit_at` (required when parked), `priority`, `notes` (markdown), `properties` (JSON), timestamps.
- **Task** — next concrete action. Belongs to a Project or directly to an Area. `title`, `status` (todo | doing | done | dropped), `due` (nullable), `effort` (S | M | L), `tags`, `sort_order`, timestamps.
- **InboxItem** — raw capture. `text`, `created_at`, `processed_at`, `converted_to` (task | project | note | discarded).
- **Review** — a weekly review session. `started_at`, `finished_at`, `notes`, `snapshot` (JSON: what was active/parked/overdue at the time), `decisions`.
- **FieldDefinition** — custom field schema per Area for Projects/Tasks (`name`, `type` text | number | date | select | checkbox, `options`), rendered generically in the UI.

Soft deletes / archive only; nothing is hard-deleted from the UI.

## Rules the app enforces

- **WIP limit:** max 3 active projects (configurable). Activating a 4th requires parking one first — the UI makes me choose.
- **Parked means decided:** a parked project must have a `revisit_at`. When that date passes, the project appears in "Decide".
- **Every active project needs a next action:** an active project with zero open tasks is flagged in Area health and in the review.
- **Inbox hygiene:** the weekly review warns (soft, overridable) if unprocessed inbox items are older than 7 days.

## Views (v1)

1. **Today** — overdue + due today + "doing" tasks, grouped by area; pinned quick-capture box that writes to Inbox.
2. **Week** — tasks due this week; active projects with their next milestone / deadline.
3. **Projects board** — kanban by status (Active / Parked / Done), filterable by area, WIP indicator always visible, drag between columns.
4. **Decide** — parked projects whose `revisit_at` has passed, oldest first.
5. **Areas** — one card per area: active project count, open task count, last touched, nearest deadline, health flags. This is the "see everything" screen.
6. **Inbox** — process each item: → task, → project, → note on a project, or discard.
7. **Weekly Review** — a guided wizard: (1) process inbox, (2) walk each active project: progress? next action? still active?, (3) go through Decide, (4) choose next week's ≤3 active projects, (5) note and finish; writes a Review record.
8. **Settings** — areas CRUD, field definitions, WIP limit, export / import.

## Scope

**v1 (ship this, then I start using it):** everything above.

**v2 (do NOT build or scaffold now; just keep the design open for it):** Finance module (budget lines vs monthly actuals), weekly summary notification (email / Telegram / web push), an MCP server exposing read/write operations so an AI assistant can add tasks and run the review with me, recurring tasks, Google Calendar deadline sync.

**Never:** multi-user, collaboration, native mobile app, time tracking, Gantt charts.

## Stack preferences (challenge them if something is clearly simpler)

- **Backend:** .NET (current LTS), minimal API, EF Core, SQLite as the single data file (easy to back up — the DB file *is* my data).
- **Frontend:** whatever gets a fast, mobile-first, inline-editable PWA quickest and stays maintainable by one person. Blazor is familiar to me; a small SPA (React / Vue / Svelte) is acceptable if you argue it's better for this UI. One choice, justified.
- **Deployment:** Docker Compose, one container plus the SQLite file in a mounted volume, runs on a cheap VPS or a home machine. Default access over a private network (e.g., Tailscale) rather than public exposure; if public, single-user login + HTTPS. Automated daily backup of the DB file.
- **Tests:** unit tests for the WIP rule, parked / revisit logic, review-wizard state machine, and export → import round-trip.

## Seed data (create a seed script; I'll finalize statuses)

- Area **Day job** (work): [add current projects]
- Area **IvnoWorks** (business): Vektralog · Probity · Last Pixel · ivnoworks.com · M&M's Skins
- Area **Startup** (startup): AI discovery scanner MVP
- Area **Marketing side work** (business): Yallabama ads · PPC clients
- Area **Home** (home): Garden decking · Holon apartment search · Home tasks
- Area **Household finance** (finance): House economic plan (a project for now; the Finance module is v2)
- Area **Health** (health): Gym

Active (max 3): [pick three]. Everything else starts **parked** with `revisit_at` = [date, e.g. 2026-12-01].

## What I want from you in this plan — before any code

1. Stack decision, with two rejected alternatives and why.
2. Schema (entities, indexes, the JSON custom-fields approach) and migration strategy.
3. API surface, briefly.
4. Screen-by-screen description at wireframe level: component breakdown and how inline editing works on touch.
5. Milestones, each sized to one evening (~2–3 h), ordered so the app is usable on my phone as early as possible. Milestone 1 must end with data model + Projects board + Tasks + Inbox + JSON export working end-to-end. I will start using it before milestone 2.
6. Test plan mapped to the rules above.
7. Dev / deploy setup: one command to run locally, one to deploy, the backup approach.
8. Risks, assumptions, open questions. Ask me anything ambiguous — ask rather than assume.
9. A proposed `CLAUDE.md` for this repo (conventions, how to run, how to test, the non-negotiables above).

Then stop and wait. Do not write application code until I reply "approved".

## Working agreement once approved

- Small, focused commits with clear messages. One milestone per branch / PR.
- No dependency added without naming it and its reason.
- No external network calls from the app. No telemetry.
- After each milestone, tell me exactly how to run it and test it on my phone.
- If scope creep appears — including from me — say so and point at this file.
