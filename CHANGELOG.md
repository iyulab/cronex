# Changelog

All notable changes to Cronex are documented here.

Versions are `0.x` and breaking changes may occur in a minor bump. Dependency floors are treated as
part of the public surface: raising one can break a consumer's restore, so a raise is called out
here even when no code changed.

## 0.4.0

### Fixed

- **Breaking**: `stagger` offsets are now computed with a stable FNV-1a hash over the trigger ID's
  UTF-8 bytes instead of `string.GetHashCode()`. Even via the `StringComparison` overload,
  `GetHashCode()` uses .NET's randomized string hashing (Marvin32 with a per-process random seed) —
  the overload only changes comparison rules, not the seed. `stagger` is documented as "deterministic
  fixed offset ... based on trigger ID", but the old implementation gave every trigger ID a different
  offset on every process restart, and a different offset on every node in a multi-instance
  deployment — the exact stampede-avoidance scenario `stagger` exists for. The computed offset value
  for a given trigger ID changes as a result; anything asserting a specific stagger offset (there is
  no supported way to do this today — offsets are internal) would need to be updated. Golden values
  for representative IDs are locked by `StaggerHashTests`.
- `jitter` was redrawn on every poll of the same still-pending occurrence instead of once — the
  effective delay was `U[0, jitter)` retried every ~1s until a draw happened to be below the elapsed
  time, which skews the actual distribution well below the documented `U[0, jitter)` median. Jitter
  is now drawn once, when `NextFireTime` is set (registration, or recomputed after firing/skip), and
  held for that occurrence.
- The tick loop polled on a fixed 1-second `Task.Delay`, so sub-second `window`/`jitter` options were
  effectively unusable (a due-and-expired window could go unnoticed for up to a second) and a slow
  handler added directly to the next poll's delay with no correction. The loop now waits exactly
  until the nearest upcoming trigger's effective fire time (nominal occurrence + stagger + jitter),
  clamped to `[10ms, 1s]`.

### Added

- `ExpressionValidator.Validate` gained an optional `referenceTime` parameter (defaults to
  `DateTimeOffset.UtcNow`, same convention as `OnceSchedule.TryParse`) and two new error codes:
  - `E018` — an `@once` absolute datetime that has already passed. Previously this parsed
    successfully, registered successfully, and then silently never fired (`GetNextOccurrence`
    returns `null` for a past one-shot time) — the worst kind of failure, one that looks like
    success.
  - `E019` — a cron day-of-month/month combination that can never occur on any calendar (e.g.
    `30 2` — February 30th). Previously this also parsed successfully and silently never fired.
    Deliberately does not flag `29 2` (February 29th) — it's valid in leap years.
- Also fixed: `docs/specification.md`'s validation-rules table documented `E021` ("`max` must be
  positive") as if it were reachable. It never was — `max <= 0` is already rejected one layer
  earlier, during option parsing, as `E016`. Corrected the table; `E021` is reserved and unused.
- New `catchup` option (`{catchup:all|skip|once}`, default `all`) makes misfire behavior explicit
  instead of implicit. Previously, occurrences missed while the loop wasn't ticking (or a prior
  handler was still running) always fired one per subsequent tick with no way to opt out — the only
  behavior available is now the explicit `all` default. `skip` discards the whole missed backlog and
  resumes at the next occurrence after now; `once` fires only the most recent missed occurrence.
  Invalid `catchup` values are rejected by `ExpressionValidator` as `E016`.
- `?` is now accepted in the day-of-month and day-of-week cron fields as a synonym for `*` (Quartz's
  "don't care" token, used to sidestep the DOM/DOW OR-semantics ambiguity — e.g. `0 0 12 ? * MON`).
  Cronex already borrowed `L`/`W`/`LW`/`L-N`/`NW`/`#`/`DOWL` from Quartz; `?` was the one common
  Quartz token that previously failed to parse, blocking a straight paste of many real-world Quartz
  expressions.
- CI now runs on a `windows-latest` matrix leg alongside `ubuntu-latest` — this library's highest-
  risk area (timezone/DST handling, spec §3.5) had never actually run on Windows before. CI also
  gates on `dotnet format --verify-no-changes` and a `dotnet pack` build-validation (no artifact
  upload — CI validates, it doesn't publish). Neither check ran before, which is exactly how the
  README/`PackageId` mismatch fixed earlier in this release reached `main` unnoticed. Fixed the one
  real formatting violation the new gate found: 69 indentation errors in `CronexExpression.cs`'s two
  `switch` statements (block-braced `case` content wasn't indented one level deeper, contrary to
  `.editorconfig`) — whitespace-only, confirmed with `git diff --ignore-all-space` before committing.
- `Cronex.Net` now declares `<IsAotCompatible>true</IsAotCompatible>` and ships
  `TriggerDefinitionJsonContext`, a source-generated `JsonSerializerContext` for
  `TriggerDefinition`. Previously "JSON-serializable" only worked through reflection-based
  `System.Text.Json`, which Native AOT/trimming doesn't support — nothing verified this actually
  worked under AOT. Verified with a real `dotnet publish -r win-x64 -p:PublishAot=true` smoke app
  referencing this package: publishes with zero trim/AOT warnings, and the resulting
  fully-native, no-runtime-required executable correctly serializes/deserializes a
  `TriggerDefinition` through the new context and parses/evaluates a `CronexExpression`.
- `CronexScheduler.Update(id, expression, [handler], [referenceTime])` — 4 overloads (string or
  pre-parsed `CronexExpression`, keeping or replacing the handler) replace a trigger's expression in
  place. Previously the only way to change a running trigger's schedule was `Unregister` + `Register`,
  which creates a brand-new `TriggerRegistration` and resets `FireCount`/`LastFired` — a `{max:N}`
  trigger's count silently broke across every config reload. `Update` keeps the same registration,
  preserving both. `TriggerRegistration.Expression`/`Handler` are now mutable (lock-protected, like
  the fields that were already mutable after construction) to support this — no observable change
  for existing read-only usage.

## 0.3.3

### Fixed

- The tick loop no longer stops permanently when an event subscriber (`TriggerFiring`,
  `TriggerCompleted`, `TriggerFailed`, `TriggerSkipped`) throws. Previously an uncaught subscriber
  exception faulted the internal loop task; nothing observed the fault, `Start()` silently became a
  no-op afterward (the running-guard was still set), and every trigger stopped firing with no
  indication why. Each event invocation is now isolated, and a new `SchedulerFaulted` event reports
  any error the loop itself catches while continuing to run.
- A `TriggerCompleted` subscriber that threw was misattributed as a handler failure — the same
  `catch (Exception)` block around the handler call also covered the `TriggerCompleted` invocation,
  so a successful run could still fire `TriggerFailed`. `TriggerCompleted`/`TriggerFailed` now
  reflect only the handler's own outcome.
- Two concurrent `TickAsync` calls (e.g. a manual call racing the automatic loop) observing the same
  due trigger could both fire it once each, since the "is it due" read and the "claim it" write to
  `NextFireTime` were separate locked operations with a gap between them. Claiming an occurrence is
  now a single atomic compare-and-clear (`TriggerRegistration.TryClaim`).
- A disabled trigger left past its scheduled time re-reported `TriggerSkipped("disabled")` on every
  tick indefinitely, because `NextFireTime` was never advanced while disabled. It now advances past
  every occurrence already missed in a single tick, so a long-disabled trigger reports the skip once
  per backlog instead of once per poll.

### Added

- `CronexScheduler.SchedulerFaulted` — fired when the internal tick loop catches an unexpected error;
  the loop keeps running afterward.
- `CronexScheduler.IsRunning` — whether the automatic tick loop (via `Start()`) is currently active.

- `Cronex.Net.Hosting` declared `Microsoft.Extensions.Hosting.Abstractions` with the floating range
  `10.*`, so the floor written into the published nuspec was whatever version happened to be latest
  when the package was packed. The floor therefore moved between releases with no corresponding
  change in the source tree, and a consumer pinned below the new floor hit `NU1109` on restore with
  nothing in the diff or the release notes to explain it.

  The reference is now an exact lowest supported version, `10.0.0`. This **lowers** the floor
  relative to 0.3.2 (see below), so any consumer that could restore 0.3.2 can also restore 0.3.3.

  The test project references `Microsoft.Extensions.Hosting` at the same `10.0.0`, so the declared
  floor is exercised by the test run rather than only asserted in package metadata. A future need
  for a newer API now fails the build instead of silently moving the floor for consumers.

- Pinned the remaining floating ranges in the test and benchmark projects to the versions they were
  already resolving to. `Deterministic` builds were being restored against ranges that change
  underfoot.

### Added

- This changelog. Its absence is why the 0.3.2 floor move had nowhere to be recorded.

## 0.3.2

### Added

- The README is packed inside both packages, so the gallery page is no longer blank.

### Changed

- **Unintended, recorded retroactively:** the `Microsoft.Extensions.Hosting.Abstractions` floor of
  `Cronex.Net.Hosting` moved from `10.0.8` to `10.0.10`. This was a side effect of the floating
  `10.*` reference resolving at pack time, not a deliberate raise, and it was announced as having no
  behavioural change. Consumers pinned below `10.0.10` cannot restore 0.3.2 and should use 0.3.3,
  whose floor is `10.0.0`.

## 0.3.1

- Packages published under the `Cronex.Net` / `Cronex.Net.Hosting` ids.
