---
id: DP-0004
status: accepted
title: Windows Reminders tool with built-in Pomodoro
components:
  - reminder-contracts
  - reminder-shared
  - pomodoro-shared
  - pomodoro-windows
  - pomodoro-tests
  - windows-toolbox
  - todo-windows
  - todo-sticky-windows
adrs:
  - docs/adr/0008-local-reminder-and-toolbox-wake-boundary.md
---

# Problem and users

Users need a local Reminders tool that works with Todo tasks and reliably surfaces reminders
while the Toolbox remains in the notification area. Pomodoro is a feature inside Reminders,
alongside standalone reminders and daily digests. Existing Todo intentionally excludes focus
time and must retain its JSON compatibility.

## Goals and non-goals

Provide standalone and Todo-linked reminders and daily start/due summaries as the default home,
with local Pomodoro phases and lightweight session history accessible inside the same application.
Cloud sync, AI, collaboration, calendar integration, productivity
scoring, generic alarm services, and a Todo JSON migration are out of scope.

## Repository and component boundaries

`PomodoroWorkspace` owns focus state. Reminder contracts/shared owns pure reminder persistence and
rules without a Todo dependency. Todo windows use the reminder service through narrow application
coordination; Toolbox only receives a validated next-wake registration and launches the known
Pomodoro executable. Windows notification and process adapters remain in owning Platform folders.

## Interfaces and data flow

Pomodoro writes focus state and reminder definitions before publishing snapshots. It calculates one
next wake, registers it with the resident Toolbox, and the Toolbox launches `--dispatch-due` at that
time. The dispatcher reloads Todo before notification delivery, persists delivery state, then
registers the next wake. Todo mutations reconcile linked reminder suspension without embedding
reminder records in Todo JSON.

## Failure, cancellation, and atomicity

Each JSON store uses a named mutex and atomic replacement. A Todo mutation is never rolled back
when reminder reconciliation fails; the caller receives an explicit pending-sync state and dispatch
rechecks task state before delivery. Corrupt reminder data is backed up and opened read-only.

## Compatibility, migration, and rollback

The application is displayed as Reminders; its existing `pomodoro` catalog ID, executable names,
source projects, installation directories, and local data paths remain stable. This is a product
navigation change within the registered components, with no new process, service, or migration.
New V1 Pomodoro and Reminder files are additive. Todo paths and JSON fields remain unchanged.
Removing the feature leaves Todo data intact; old Todo builds cannot erase reminder data because
they never write the separate Reminder store.

## Security, privacy, dependencies, and permissions

All content remains local. The Toolbox wake channel only accepts the fixed `pomodoro` tool ID and
fixed dispatch verbs. Windows App SDK notifications are the existing dependency; no new package,
network permission, elevated execution, or Core protocol is added.

## Test and acceptance plan

Test phase transitions, persisted recovery, recurrence, reminder uniqueness, task suspension and
restore, daily-digest filtering, catch-up aggregation, atomic persistence, Toolbox wake validation,
and notification action routing. Verify both build configurations, architecture checks, visual
states, keyboard navigation and accessibility names.

## Reuse and duplication analysis

Reuse TodoStore read/query semantics, Todo task completion commands, Toolbox process resolution,
Windows App SDK and the existing Fluent window conventions. The reminder store is deliberately
separate so Todo does not duplicate timer state or receive a fragile schema extension.
