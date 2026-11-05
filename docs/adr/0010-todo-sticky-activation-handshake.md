# ADR-0010: Confirm Todo Sticky activation before hiding the main window

## Status

Accepted.

## Context

Todo's WinUI main window and WPF Sticky window run as separate single-instance processes. The
main process previously treated `Process.Start` or a successful named-event signal as proof that
Sticky was visible. During a cold start, startup failure, or a rapid return-to-main and reopen
sequence, the old Sticky process could still be exiting after accepting the signal. The main
window then hid while no Sticky window was available.

## Decision

Todo serializes Sticky startup, activation, and shutdown transitions in its existing process
coordinator. A newly launched Sticky process starts hidden and signals readiness after its WPF
dispatcher begins processing. The main process then requests activation and waits for an
acknowledgement sent only after Sticky has restored and activated its window. The main window hides
only after that acknowledgement. Shutdown waits for the previous Sticky process to exit before a
new startup can proceed.

All waits are asynchronous and bounded. A failed or timed-out activation leaves the Todo main
window visible and restores normal-mode settings. The coordinator does not force-terminate a
stalled process.

Leaving floating mode on a hidden, prewarmed WPF window only restores its layout. It must not call
`Activate()` before the window's first `Show()`. External activation shows the restored window
before requesting focus and sending the acknowledgement.

## Consequences

- Rapid mode switching can wait briefly for the previous Sticky process to finish, but cannot
  strand the user with both windows hidden.
- Cold startup is considered successful only after the Sticky UI confirms activation.
- Executable names, local data paths and formats, UI commands, and repository boundaries remain
  unchanged.
- The additional readiness and acknowledgement events are local, per-user process coordination
  details and carry no task data.
