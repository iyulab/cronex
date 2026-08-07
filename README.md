# Cronex

[![NuGet](https://img.shields.io/nuget/v/Cronex.Net.svg)](https://www.nuget.org/packages/Cronex.Net)
[![NuGet Downloads](https://img.shields.io/nuget/dt/Cronex.Net.svg)](https://www.nuget.org/packages/Cronex.Net)
[![CI](https://github.com/iyulab/Cronex/actions/workflows/ci.yml/badge.svg)](https://github.com/iyulab/Cronex/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

Extended cron expressions for .NET. Parse, validate, trigger — your app handles the rest.

```csharp
var scheduler = new CronexScheduler();

scheduler.Register("report", "TZ=Asia/Seoul 0 9 * * MON-FRI {jitter:30s}", async (ctx, ct) =>
{
    // ctx carries everything: trigger ID, scheduled time, metadata, expression...
    await GenerateReport(ctx.ScheduledTime, ct);
});

scheduler.Start();
```

## What Cronex Is

Cronex is a **cron expression superset**: standard cron syntax works unchanged, and everything else is additive within the same string.

```
TZ=Asia/Seoul 0 9 * * MON-FRI {jitter:30s, until:2025-12-31}
│             │               │
│             │               └─ options block   ← extension
│             └─ 5-field POSIX cron               ← unchanged
└─ timezone prefix                                 ← extension
```

| | Adds | Value |
|---|------|-------|
| **Standard cron** | 5 fields (`min hour day month weekday`), ranges/lists/steps (`1-5`, `1,3,5`, `*/5`), names (`MON`, `JAN`) | Portable — any existing crontab expression works as-is |
| **`TZ=` prefix** | Timezone-aware, DST-correct evaluation | Schedules run in the business's timezone, not the server's |
| **Calendar tokens** | `L`, `W`, `LW`, `L-N`, `NW`, `#`, `DOWL`, `?` (Quartz-style, not in POSIX cron) | "Last weekday of month", "2nd Monday" — no app-side date math. `?` is a drop-in synonym for `*` in day-of-month/day-of-week, for pasting in Quartz expressions unchanged |
| **`@every` / `@once` / `@daily` ...** | Interval and one-shot shorthand | Express recurring or single-fire schedules without hand-translating to cron fields |
| **`{...}` options** | `jitter`, `stagger`, `window`, `from`, `until`, `max`, `tag`, `catchup` | Execution-time controls — collision avoidance, deadlines, run caps, misfire policy — normally re-implemented per app |

When a trigger fires, your handler gets a rich `TriggerContext`. That's it.

**What Cronex is not:** a job framework, a task queue, or a persistence layer. It doesn't tell you how to execute work. It tells you *when*, gives you *context*, and gets out of the way. See the [persistence boundary](docs/specification.md#74-persistence-boundary) for how to build storage/rehydration on top.

Works well for AI agent runtimes that need to generate, validate, and hand back schedules as plain strings — no code, just data.

## Install

```bash
dotnet add package Cronex.Net          # Core: expression + scheduler
dotnet add package Cronex.Net.Hosting  # Optional: Generic Host integration
```

The core package has **zero external dependencies**.

## Expression

### Quick Reference

| Pattern | Meaning |
|---------|---------|
| `*/5 * * * *` | Every 5 minutes |
| `0 9 * * MON-FRI` | Weekdays at 09:00 |
| `TZ=UTC 0 0 * * *` | Midnight UTC |
| `@every 30m` | Every 30 minutes |
| `@every 1h-2h` | Random 1–2 hour interval |
| `@once 2025-03-01T09:00:00+09:00` | One-shot at absolute time |
| `@once +20m` | One-shot 20 minutes from now |
| `@daily` | Alias for `0 0 * * *` |
| `0 0 L * *` | Last day of month |
| `0 0 * * MON#2` | Second Monday |
| `0 0 12 ? * MON` | Quartz-style — `?` in day-of-month is a synonym for `*` |

### Options `{...}`

| Key | Type | Effect |
|-----|------|--------|
| `jitter` | duration | Random delay `[0, value)` per execution (non-deterministic) |
| `stagger` | duration | Fixed offset per trigger, deterministic collision avoidance |
| `window` | duration | Skip if handler can't start in time |
| `from` | date/datetime | Ignore occurrences before this |
| `until` | date/datetime | Ignore occurrences after this |
| `max` | int | Stop after N executions |
| `tag` | string | Metadata tags (`+` separated) |
| `catchup` | `all` \| `skip` \| `once` | How to handle occurrences missed while the loop wasn't ticking. Default `all` fires every one; `skip` discards the whole backlog; `once` fires only the most recent missed occurrence |

Durations: `500ms`, `30s`, `5m`, `2h`, `1d`, `1h30m`

> **`jitter` vs `stagger`:** jitter adds a random delay each execution — non-deterministic. stagger assigns a fixed offset at registration time based on trigger ID — deterministic. Use stagger to prevent top-of-hour stampedes; use jitter to spread load unpredictably.

## Parse & Validate

```csharp
// Parse
var expr = CronexExpression.Parse("TZ=Asia/Seoul 0 9 * * MON-FRI");
var next = expr.GetNextOccurrence(DateTimeOffset.UtcNow);
var upcoming = expr.Enumerate(DateTimeOffset.UtcNow, count: 20);

// TryParse (no throw)
if (CronexExpression.TryParse(input, out var expr, out var error))
    Console.WriteLine(expr.GetNextOccurrence(DateTimeOffset.UtcNow));

// Validate (structured errors and warnings for programmatic consumers)
var result = ExpressionValidator.Validate("0 25 * * *");
// result.IsValid → false
// result.Errors[0]:
//   Code:  "E003"
//   Field: "hour"
//   Message: "Value 25 out of range [0, 23]"
//   Value: "25"

// Warnings (non-blocking)
var result2 = ExpressionValidator.Validate("@every 10m {jitter:6m}");
// result2.IsValid → true
// result2.Warnings[0]:
//   Code: "E022" — jitter exceeds 50% of schedule interval

// Human-readable description
var expr2 = CronexExpression.Parse("TZ=Asia/Seoul 0 9 * * MON-FRI");
Console.WriteLine(expr2.Describe());
// "At 09:00, Monday through Friday (Asia/Seoul)"
```

## Trigger & Schedule

### Inline Handler

```csharp
var scheduler = new CronexScheduler();

scheduler.Register("cleanup", "0 3 * * *", async (ctx, ct) =>
{
    Console.WriteLine($"[{ctx.TriggerId}] at {ctx.ActualTime}");
    await CleanupAsync(ct);
});

scheduler.Start();

// Don't forget to stop and dispose
await scheduler.StopAsync();
await scheduler.DisposeAsync();
```

### With Metadata

```csharp
var definition = new TriggerDefinition
{
    Id = "sync",
    Expression = "@every 15m",
    Metadata = new() { ["env"] = "prod", ["endpoint"] = "https://api.example.com" }
};

scheduler.Register(definition, async (ctx, ct) =>
{
    var endpoint = ctx.Metadata["endpoint"];
    await SyncAsync(endpoint, ct);
});
```

### Events

```csharp
scheduler.TriggerFiring    += ctx => logger.LogDebug("Firing {Id}", ctx.TriggerId);
scheduler.TriggerCompleted += ctx => logger.LogInformation("{Id} done", ctx.TriggerId);
scheduler.TriggerFailed    += (ctx, ex) => logger.LogError(ex, "{Id} failed", ctx.TriggerId);
scheduler.TriggerSkipped   += (id, reason) => logger.LogWarning("{Id} skipped: {Reason}", id, reason);
scheduler.SchedulerFaulted += ex => logger.LogCritical(ex, "Tick loop caught an unexpected error");
```

A throwing event subscriber is isolated per-invocation — it cannot stop the tick loop, and it cannot
be misattributed to `TriggerFailed` (a `TriggerCompleted` subscriber that throws does not turn a
successful run into a reported failure). `SchedulerFaulted` is the escape hatch for the loop's own
unexpected errors; the loop keeps running after firing it.

### Runtime Control

```csharp
scheduler.SetEnabled("sync", false);  // pause
scheduler.SetEnabled("sync", true);   // resume
scheduler.Unregister("old-job");      // remove
scheduler.IsRunning;                  // true while Start() has an active tick loop

// Reload a changed schedule without resetting FireCount/LastFired — Unregister + Register
// would create a brand-new registration and reset FireCount to 0, breaking a {max:N} trigger's
// count across the reload.
scheduler.Update("sync", "@every 30m");                              // schedule only, keep handler
scheduler.Update("sync", "@every 30m", async (ctx, ct) => { ... });   // schedule + handler
```

## TriggerContext

This is what your handler receives. Everything needed to do the work:

```csharp
public sealed class TriggerContext
{
    string              TriggerId      // which trigger fired
    CronexExpression   Expression     // the parsed expression (inspect: .GetNextOccurrence(), .Options, .TimeZone)
    DateTimeOffset      ScheduledTime  // intended fire time (before jitter/stagger)
    DateTimeOffset      ActualTime     // actual dispatch time (after jitter/stagger)
    int                 FireCount      // 1-based total executions
    IReadOnlyDictionary Metadata       // free-form key-value from TriggerDefinition
}
```

The handler can inspect `Expression` to query future occurrences, check timezone, or read tags — all without reaching back into the scheduler.

## TriggerDefinition (JSON-serializable)

`TriggerDefinition` is a serializable trigger specification — separated from runtime concerns like handlers. This enables external systems (CLIs, APIs, config files) to create trigger definitions as JSON that consuming apps bind to handlers at runtime.

```csharp
// Serializable definition — no delegate, no runtime dependency
public sealed class TriggerDefinition
{
    public required string Id { get; init; }
    public required string Expression { get; init; }
    public bool Enabled { get; init; } = true;
    public Dictionary<string, string>? Metadata { get; init; }
}

// Consuming app binds handler at runtime
scheduler.Register(definition, async (ctx, ct) =>
{
    var target = ctx.Metadata["endpoint"];
    await SyncAsync(target, ct);
});
```

```json
{
  "id": "health-check",
  "expression": "TZ=UTC @every 15m {stagger:3m}",
  "enabled": true,
  "metadata": {
    "endpoint": "https://api.example.com/health",
    "delivery.mode": "webhook",
    "delivery.to": "https://hooks.example.com/results"
  }
}
```

This separation matters because:
- **Config files** can define schedules without code
- **APIs** can accept trigger definitions over HTTP
- **External tools** can generate definitions programmatically
- **Validation** works on the definition before any handler is involved

## Generic Host Integration

```csharp
// Program.cs
builder.Services.AddCronex(c =>
{
    // Inline
    c.AddTrigger("ping", "@every 1m",
        (ctx, ct) => { Console.WriteLine("pong"); return Task.CompletedTask; });

    // DI-resolved handler
    c.AddTrigger<ReportHandler>("report", "TZ=Asia/Seoul 0 9 * * MON-FRI");
});

// Handler class — resolved from DI per invocation
public class ReportHandler(IReportService reports) : ICronexHandler
{
    public async Task HandleAsync(TriggerContext ctx, CancellationToken ct)
    {
        await reports.GenerateAsync(ctx.ScheduledTime, ct);
    }
}
```

## Packages

| Package | Description | Dependencies |
|---------|-------------|--------------|
| **Cronex.Net** | Core: expression parser + scheduler + events | BCL only (zero external) |
| **Cronex.Net.Hosting** | Generic Host integration (`AddCronex`) | Cronex.Net, M.E.Hosting.Abstractions |

Requires **.NET 10** or later.

## Why Cronex

| | Cronex | Cronos | Quartz.NET | NCrontab |
|---|---------|--------|------------|----------|
| **Role** | Parser + mini scheduler | Parser only | Full job framework | Parser only |
| **Expression** | Standard cron + TZ, @every, @once, jitter, stagger, window, from/until/max — all in one string | 6-field cron | Cron + calendar triggers (API) | 5-field cron, 6-field opt-in |
| **Special entries** | L, W, LW, L-N, NW, #, DOWL | L, W, # | L, W, # | — |
| **DST handling** | Built-in (Vixie Cron) | Built-in | Built-in | — |
| **Dependencies** | Zero (BCL only) | Zero | Multiple | Zero |
| **Scheduler** | Lightweight, event-driven | — | Heavyweight, persistent | — |

Everything in one string — no API wrappers, no builder patterns, no config objects.

## Timezone & DST

Cronex follows Vixie Cron semantics for DST transitions:

- **Spring forward** — if a scheduled time falls in the DST gap, it advances to the next valid time
- **Fall back** — cron expressions fire once per calendar time (first occurrence); interval expressions may fire twice

See the [specification §3.5](docs/specification.md#35-dst-handling-rules) for full rules.

## Design

**String-complete.** One string = full schedule definition. Ideal for configs and code generation.

**Serializable.** `TriggerDefinition` separates *what to schedule* from *how to execute*. External systems produce JSON; your app binds handlers.

**Consumer-first.** Cronex doesn't own your execution model. Your handler is a delegate. Your DI container resolves your services. Cronex just calls you at the right time with the right context.

**Deterministic.** `expr.GetNextOccurrence(referenceTime)` is a pure function. No hidden clock access.

**Validatable.** Structured errors with codes, fields, and positions — programmatic consumers can parse and self-correct.

**Testable.** The scheduler accepts `TimeProvider` for deterministic testing.

**Observable.** Every state transition (firing, completed, failed, skipped) is an event. Build history, monitoring, and alerting on top.

**Minimal.** Core has zero dependencies. No database. No queue. No opinions.

## Metadata Conventions

Cronex passes metadata through without interpreting it. The following keys are recommended conventions for common integration patterns:

| Key | Purpose | Example |
|-----|---------|---------|
| `env` | Environment tag | `"prod"`, `"staging"` |
| `endpoint` | Target API endpoint | `"https://api.example.com"` |
| `scope` | Execution isolation hint | `"isolated"` or `"shared"` |
| `scope.session` | Session key for scope management | `"cron:health-check"` |
| `delivery.mode` | Result routing mode | `"webhook"`, `"queue"`, `"none"` |
| `delivery.to` | Result destination | `"https://hooks.example.com/results"` |
| `delivery.channel` | Notification channel | `"slack"`, `"email"` |

These are conventions, not enforcement. Cronex never reads these keys — your handler does.

```csharp
var definition = new TriggerDefinition
{
    Id = "sync",
    Expression = "@every 15m",
    Metadata = new()
    {
        ["endpoint"] = "https://api.example.com/data",
        ["env"] = "prod",
        ["delivery.mode"] = "webhook",
        ["delivery.to"] = "https://hooks.example.com/results"
    }
};

scheduler.Register(definition, async (ctx, ct) =>
{
    var endpoint = ctx.Metadata["endpoint"];
    var result = await SyncAsync(endpoint, ct);

    if (ctx.Metadata.TryGetValue("delivery.mode", out var mode) && mode == "webhook")
        await HttpPost(ctx.Metadata["delivery.to"], result, ct);
});
```

## License

MIT
