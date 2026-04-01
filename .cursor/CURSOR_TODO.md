# Cursor To-Do List — PixelVault improvements

Action plan for evolving the app (native WPF line under `src/PixelVault.Native`). Check items off as you complete them.

---

## Phase 1 — Safety net

- [ ] Add a test project (e.g. `PixelVault.Native.Tests`, `net8.0` or `net8.0-windows` as needed).
- [ ] Add tests for high-value pure logic: cache/path naming (`SafeCacheName`), index DB read/write invariants, legacy migration assumptions, critical string parsing (file lists, IDs).
- [ ] Keep a short manual golden-path checklist: Library → refresh → import one file → verify SQLite row; run after risky changes until coverage grows.

---

## Phase 2 — UI-thread responsiveness

- [ ] Audit all `TimeoutWebClient` call sites; ensure work is not blocking the WPF UI thread (background thread or `async` + dispatcher marshaling).
- [ ] Standardize long operations: background work + `Dispatcher.BeginInvoke` for UI updates; add cancel where it matters (covers, refresh, import).
- [ ] Prefer `Task.Run` + explicit cancellation tokens over ad-hoc `Task.Factory.StartNew` in new or touched code.

---

## Phase 3 — Shrink `MainWindow` / extract services

- [ ] Extract SQLite + path helpers into a dedicated type (e.g. index database service) with explicit dependencies instead of reaching into `MainWindow` fields.
- [ ] Extract cover / SteamGridDB flow into a service; keep UI as thin callbacks.
- [ ] Push more import orchestration into `Import/` partials or dedicated types; leave only UI construction in WPF code-behind where possible.

---

## Phase 4 — Nullable and modern C#

- [ ] Enable nullable context for new files or a new project first; migrate extracted services before the giant main file.
- [ ] Optionally enable implicit usings only for new projects/files to avoid a noisy repo-wide diff.

---

## Phase 5 — UX and polish (can run in parallel with 2–3)

- [ ] Improve user-facing errors: what failed, what to check (paths, token, network), not only raw `ex.Message` for deep failures.
- [ ] Align progress + cancel behavior for long scans and cover fetches.
- [ ] Quick pass: tab order, keyboard paths for common actions, high-contrast spot-checks.

---

## Deferred (revisit after the above)

- [ ] Full MVVM + DI container — only after services are extracted and stable.
- [ ] Replacing WinForms usage — only if a concrete pain point appears.
- [ ] Major UI redesign — after architecture and responsiveness are in a good place.
