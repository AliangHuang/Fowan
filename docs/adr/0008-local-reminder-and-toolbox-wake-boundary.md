# ADR-0008: Local reminder store and Toolbox wake boundary

## Status

Accepted

## Context

Todo has released local JSON data and explicitly excludes focus timers. Reminders must be editable
from Todo and Pomodoro, continue while the Pomodoro window is closed, and avoid unreliable long
Windows scheduled-toast queues.

## Decision

Store reminders separately under `%LOCALAPPDATA%\\Fowan\\Reminders`; a reminder may hold an opaque
Todo task ID. The resident Toolbox owns only a validated next-wake registration and launches the
known Pomodoro executable. Pomodoro owns occurrence calculation, Todo revalidation and Windows
notification delivery. Reminder recurrence is local wall-clock weekly/monthly recurrence.

## Consequences

Todo JSON remains compatible. Cross-store operations are eventually consistent, so dispatch always
rechecks Todo state. Toolbox must remain resident for automatic delivery; its exit explicitly pauses
delivery and the next startup performs bounded catch-up.
