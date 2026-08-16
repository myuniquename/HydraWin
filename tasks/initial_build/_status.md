# HydraWin initial build (status)

Where the initial build currently stands. The plan itself — scope, decisions, task index, ordering
and ground rules — lives in `_plan.md`; this file is the only place task status is tracked.

## At a glance

| # | Task | Status |
| --- | --- | --- |
| 01 | `01_spike_win32_assumptions.md` | done (2026-08-15, accepted 2026-08-16) |
| 02 | `02_solution_scaffold.md` | done (2026-08-15, accepted 2026-08-16) |
| 03 | `03_window_tracker.md` | done (2026-08-15, accepted 2026-08-16) |
| 04 | `04_workspace_model_persistence.md` | done (2026-08-15, accepted 2026-08-16) |
| 05 | `05_recovery_journal.md` | done (2026-08-15, accepted 2026-08-16) |
| 06 | `06_switch_engine.md` | done (2026-08-16, accepted 2026-08-16) |
| 07 | `07_ui_shell.md` | done (2026-08-16) — accepted by the user |
| 08 | `08_tray_and_hotkeys.md` | done (2026-08-16) — accepted by the user |
| 09 | `09_notifications.md` | not started |
| 10 | `10_hardening_polish.md` | not started |
| 11 | `11_promote_docs.md` | not started |

## Progress

**Tasks 01–08 are done and accepted (2026-08-16).** That is the whole of the non-negotiable core
(01–07) plus the tray, global hotkeys and single-instance behaviour. HydraWin is usable daily as it
stands: it tracks windows, switches tasks by click or hotkey, survives a crash through the journal,
and lives in the tray.

**Outstanding: 09 (notifications), 10 (hardening and polish), 11 (promote `docs/`).** Without 09
there are no badges; without 10 there is no settings UI and no rule editor — both of which several
earlier tasks defer to. Task 11 has not run, so `docs/` is still empty and this folder is still the
only home for the project's findings.

## Where the detail is

Per-task detail — what was actually done, how it differed from the plan and why, measured results,
and the file lists — lives in each task file's **Record on completion** section. The `Status:` line
at the top of each task file is the source for the table above.
