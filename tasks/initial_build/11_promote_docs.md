# Task 11 — Promote findings to docs/, delete this folder

Status: **not started**
Depends on: tasks 01–10 done (their Record-on-completion sections are the raw material).

## Motivation

Per the DocsAndTasks convention: `docs/` holds timeless findings, `tasks/` holds progress, and a
completed feature folder is **deleted, not archived** — anything worth keeping gets promoted
first. This task is that promotion for the initial build.

## Background

The convention's shape for a subsystem folder (docs never track status — no "done", no dates-as-
progress):

```
docs/<subsystem>/README.md        the hub — lead paragraph (key types in bold, ends "This folder
                                  is the canonical documentation for X."), a | Doc | Read it for |
                                  table, a Related: line, ## What it does (one screen),
                                  ## Component map (fenced ASCII), ## Key files (| Purpose | File |)
docs/<subsystem>/architecture.md  components, data flow, storage, validation
docs/<subsystem>/how_to.md        recipes, each with its own verification step
docs/<subsystem>/reference.md     API surface, payloads, settings, naming
```

`CLAUDE.md` § *Where to read* is the only index — update it; no root `docs/` index.

## Work

### A. Write the doc folders
Three subsystems (merge to two if the content turns out thin — judge by what got built):
- `docs/workspaces/` — tracker + model + switch engine + journal. Architecture must carry: the
  trackable-window filter as implemented; the journal-before-hide invariant and its exact write/
  confirm lifecycle; the switch algorithm's step order and why; identity validation on restore;
  the app-quirk table from tasks 01/06 (which apps hide cleanly, Teams behaviour, refuses-hide
  handling); the OS-virtual-desktop rejection rationale (from `_plan.md`, so it survives this
  folder's deletion). `how_to.md`: the crash drill as a recipe; adding support for a stubborn
  app. `reference.md`: `state.json` / `journal.json` schemas, `--restore-all`, hotkey defaults.
- `docs/notifications/` — signal sources and their limits (the spike's flash-while-hidden
  verdict), rule model, edge-triggering, clearing matrix, the shipped regexes verbatim (from
  task 09's record) with per-app title-format notes.
- `docs/ui/` (or fold into workspaces if thin) — MVVM layering rule (no Win32 above Core),
  drag-and-drop data contract, tray/single-instance/IPC mechanism chosen in task 08.

### B. Update `CLAUDE.md`
- § *Where to read*: replace the `tasks/initial_build/` rows with the `docs/` folders table.
- § *Gotchas*: re-point the rejection-rationale reference from `_plan.md` to its new home in
  `docs/workspaces/architecture.md`.

### C. Delete `tasks/initial_build/`
After A and B are complete and reviewed against rule 1 (nothing status-flavoured got promoted;
no durable finding left behind — sweep every task's *Record on completion* once more), delete
the whole folder including `screenshots/`. Older links pointing here going dead afterwards is
the convention working, not rot.

## Verification

- Every `docs/**/*.md` contains no status language (grep for `not started|in progress|done (`
  — zero hits) and every README follows the six-part hub shape.
- `CLAUDE.md` § *Where to read* resolves: each referenced file exists; no reference to
  `tasks/initial_build` remains anywhere in the repo (grep — zero hits after deletion).
- A cold-start test: ask an agent with no prior context to answer "how does HydraWin guarantee a
  crash can't lose windows?" and "which apps don't hide cleanly?" from `docs/` alone — both
  answerable without this folder.

## Record on completion

*(This folder is deleted on completion — put the completion summary in the change report to the
user instead: what was promoted where, what was judged not worth keeping, and the full list of
new / modified / deleted files.)*
