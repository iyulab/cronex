# Cronex Expression Specification v1.0

## 1. Overview

A Cronex Expression is a **superset** of the standard Unix cron expression.
Existing cron expressions work unchanged. Additive extensions enable
timezone, interval, one-shot (absolute/relative), jitter, stagger, window, expiry, start date, and max execution count — all in a single string.

### Design Principles

1. **Standard-compatible**: Any valid 5-field or 6-field cron expression is a valid Cronex expression.
2. **String-complete**: A single string fully describes every scheduling condition.
3. **Deterministic**: Same expression + same reference time → always the same next occurrence.
4. **Parser-friendly**: Extension options live in a `{}` block, cleanly separated from the schedule body.
5. **Round-trippable**: `Parse(expr).ToString()` produces a semantically equivalent string.

---

## 2. Formal Grammar (EBNF)

```ebnf
(* ===== Top-Level ===== *)

Cronex_expression
    = [ timezone_prefix ] , schedule_body , [ options_block ] ;

(* ===== Timezone Prefix ===== *)

timezone_prefix
    = "TZ=" , iana_timezone , whitespace ;

iana_timezone
    = identifier , { "/" , identifier } ;
    (* e.g. "Asia/Seoul", "America/New_York", "UTC" *)

(* ===== Schedule Body ===== *)
(* Mutually exclusive: exactly one of cron, alias, interval, once *)

schedule_body
    = cron_expression          (* standard cron *)
    | alias_expression         (* @daily, @hourly, etc. *)
    | interval_expression      (* @every 30m *)
    | once_expression ;        (* @once 2025-03-01T09:00:00+09:00 *)

(* ----- Standard Cron ----- *)

cron_expression
    = cron_5field | cron_6field ;

cron_5field
    = minute_field , whitespace ,
      hour_field , whitespace ,
      dom_field , whitespace ,
      month_field , whitespace ,
      dow_field ;

cron_6field
    = second_field , whitespace ,
      minute_field , whitespace ,
      hour_field , whitespace ,
      dom_field , whitespace ,
      month_field , whitespace ,
      dow_field ;

(* ----- Cron Fields ----- *)

second_field  = cron_field ;   (* 0-59 *)
minute_field  = cron_field ;   (* 0-59 *)
hour_field    = cron_field ;   (* 0-23 *)
dom_field     = dom_cron_field ;  (* 1-31, with L/W support *)
month_field   = month_cron_field ;  (* 1-12 or JAN-DEC *)
dow_field     = dow_cron_field ;  (* 0-7, SUN-SAT, with L/# support *)

cron_field
    = "*"
    | cron_list ;

cron_list
    = cron_element , { "," , cron_element } ;

cron_element
    = cron_value , [ "-" , cron_value ] , [ "/" , positive_integer ]
    | "*" , "/" , positive_integer ;

cron_value
    = integer ;

(* Day-of-Month: additionally supports L, W *)
dom_cron_field
    = cron_field
    | "L"                     (* last day of month *)
    | "LW"                    (* nearest weekday to the last day *)
    | "L-" , positive_integer (* N days before the last day *)
    | integer , "W" ;         (* nearest weekday to the Nth day *)

(* Month: numeric or name *)
month_cron_field
    = cron_field
    | month_name_list ;

month_name_list
    = month_name_element , { "," , month_name_element } ;

month_name_element
    = month_name , [ "-" , month_name ] , [ "/" , positive_integer ] ;

month_name
    = "JAN" | "FEB" | "MAR" | "APR" | "MAY" | "JUN"
    | "JUL" | "AUG" | "SEP" | "OCT" | "NOV" | "DEC" ;

(* Day-of-Week: numeric/name + L/# support *)
dow_cron_field
    = cron_field
    | dow_name_list
    | dow_name , "L"              (* last occurrence of that weekday in the month *)
    | dow_name , "#" , digit ;    (* Nth occurrence of that weekday in the month *)

dow_name_list
    = dow_name_element , { "," , dow_name_element } ;

dow_name_element
    = dow_name , [ "-" , dow_name ] , [ "/" , positive_integer ] ;

dow_name
    = "SUN" | "MON" | "TUE" | "WED" | "THU" | "FRI" | "SAT" ;

(* ----- Aliases ----- *)

alias_expression
    = "@yearly" | "@annually"     (* = 0 0 1 1 * *)
    | "@monthly"                  (* = 0 0 1 * * *)
    | "@weekly"                   (* = 0 0 * * 0 *)
    | "@daily" | "@midnight"      (* = 0 0 * * * *)
    | "@hourly" ;                 (* = 0 * * * * *)

(* ----- Interval ----- *)

interval_expression
    = "@every" , whitespace , duration
    | "@every" , whitespace , duration , "-" , duration ;
    (* "@every 30m"      → fixed 30-minute interval *)
    (* "@every 1h-2h"    → random interval between 1 and 2 hours *)

(* ----- Once ----- *)

once_expression
    = "@once" , whitespace , iso8601_datetime
    | "@once" , whitespace , relative_duration ;
    (* "@once 2025-03-01T09:00:00+09:00" — absolute time *)
    (* "@once +20m" — relative to reference time *)

relative_duration
    = "+" , duration ;
    (* "+20m", "+1h30m", "+2h" *)


(* ===== Options Block ===== *)

options_block
    = whitespace , "{" , option_list , "}" ;

option_list
    = option , { "," , option } ;

option
    = whitespace_opt , option_key , ":" , whitespace_opt , option_value , whitespace_opt ;

option_key
    = "jitter"       (* non-deterministic random delay: duration *)
    | "stagger"      (* deterministic fixed offset: duration — collision avoidance *)
    | "window"       (* execution window: duration *)
    | "from"         (* start date: ISO 8601 date or datetime *)
    | "until"        (* expiry date: ISO 8601 date or datetime *)
    | "max"          (* max execution count: positive integer *)
    | "tag" ;        (* tag: arbitrary string, multiple values joined with + *)

option_value
    = duration                (* jitter, window *)
    | iso8601_date            (* from, until — date only *)
    | iso8601_datetime        (* from, until — with time *)
    | positive_integer        (* max *)
    | tag_value ;             (* tag *)

tag_value
    = identifier , { "+" , identifier } ;
    (* e.g. "report+daily" → ["report", "daily"] *)


(* ===== Primitives ===== *)

duration
    = positive_integer , duration_unit , { positive_integer , duration_unit } ;
    (* compound duration: "1h30m", "2h", "30s", "500ms" *)

duration_unit
    = "ms"      (* milliseconds *)
    | "s"       (* seconds *)
    | "m"       (* minutes *)
    | "h"       (* hours *)
    | "d" ;     (* days *)

iso8601_datetime
    = date_part , "T" , time_part , [ timezone_offset ] ;

iso8601_date
    = date_part ;

date_part
    = year , "-" , month_num , "-" , day_num ;

time_part
    = hour_num , ":" , minute_num , ":" , second_num ;

timezone_offset
    = "Z"
    | ( "+" | "-" ) , hour_num , ":" , minute_num ;

year       = digit , digit , digit , digit ;
month_num  = digit , digit ;
day_num    = digit , digit ;
hour_num   = digit , digit ;
minute_num = digit , digit ;
second_num = digit , digit ;

positive_integer = digit_nonzero , { digit } | "0" ;
integer          = [ "-" ] , positive_integer ;
identifier       = letter , { letter | digit | "_" | "-" } ;
whitespace       = " " , { " " } ;
whitespace_opt   = { " " } ;
digit            = "0" | "1" | "2" | "3" | "4" | "5" | "6" | "7" | "8" | "9" ;
digit_nonzero    = "1" | "2" | "3" | "4" | "5" | "6" | "7" | "8" | "9" ;
letter           = "A"-"Z" | "a"-"z" ;
```

---

## 3. Semantics

### 3.1 5-Field vs 6-Field Detection

The parser determines 5-field (minute–dow) vs 6-field (second–dow) by counting whitespace-separated fields.
If an options block `{...}` is present, only fields before it are counted.

| Field count | Interpretation |
|-------------|----------------|
| 5 | `minute hour dom month dow` |
| 6 | `second minute hour dom month dow` |

The second field of a 5-field expression is implicitly `0`.

### 3.2 Value Ranges

| Field | Range | Special characters |
|-------|-------|--------------------|
| second | 0–59 | `*` `,` `-` `/` |
| minute | 0–59 | `*` `,` `-` `/` |
| hour | 0–23 | `*` `,` `-` `/` |
| day of month | 1–31 | `*` `,` `-` `/` `L` `W` |
| month | 1–12 or JAN–DEC | `*` `,` `-` `/` |
| day of week | 0–7 (0,7=SUN) or SUN–SAT | `*` `,` `-` `/` `L` `#` |

### 3.3 Reversed Ranges

`23-01` is equivalent to `23,00,01`. `FRI-MON` is equivalent to `FRI,SAT,SUN,MON`.
This allows natural expression of ranges that cross midnight or wrap around the week.

### 3.3.1 DOM/DOW Combined Semantics (Vixie Cron OR Rule)

When both the day-of-month (DOM) and day-of-week (DOW) fields are non-wildcard (not `*`),
the **OR** semantic is applied per the Vixie Cron standard:

| DOM | DOW | Matching rule |
|-----|-----|---------------|
| `*` | `*` | Every day |
| `*` | non-wildcard | Check DOW only |
| non-wildcard | `*` | Check DOM only |
| non-wildcard | non-wildcard | Fire if DOM **or** DOW matches |

Example: `0 0 1,15 * FRI` → fires on the 1st, 15th, **and** every Friday of each month (OR, not AND).

### 3.4 Timezone (`TZ=`)

When a `TZ=` prefix is present, the expression is interpreted in the specified IANA timezone.
When absent, the expression is interpreted in the timezone provided by the caller (or UTC by default).

The expression-level timezone affects next-occurrence calculation, DST transition handling, and the offset of result times.

### 3.5 DST Handling Rules

Follows Vixie Cron semantics:

**Spring Forward (clock moves ahead):**
- Non-interval expressions that map to an invalid time → adjusted to the next valid time.
- Interval expressions → no occurrences are skipped (fire at the adjusted time).

**Fall Back (clock moves back):**
- Non-interval expressions → only the first occurrence fires (prevents duplicates).
- Interval expressions → both occurrences fire (no skipping).

**Definition of non-interval expressions:** Expressions where none of the second, minute, or hour fields contain `*`, a range, or a step.
Example: `0 30 1 * * *` (non-interval), `*/5 * * * *` (interval).

### 3.6 `@every` (Interval)

`@every <duration>` is a fixed-interval schedule. Unlike cron, it is not calendar-aligned.
- The first occurrence is at scheduler start time + duration.
- Subsequent occurrences are at the previous **scheduled time** (not actual execution time) + duration.

`@every <min>-<max>` picks a uniformly random interval between min and max for each cycle.
Unlike jitter, the interval itself varies each time.

### 3.7 `@once` (One-shot)

`@once` fires exactly once. Two forms are supported:

**Absolute time:** `@once <iso8601>` — fires at the specified instant.
If the specified time is already in the past, `GetNextOccurrence()` returns `null`.

**Relative time:** `@once +<duration>` — fires at the reference time plus the given duration.

```
@once +20m         → reference time + 20 minutes
@once +1h30m       → reference time + 1 hour 30 minutes
@once +2h          → reference time + 2 hours
```

Relative durations are resolved to absolute times at parse time. For determinism, pass an explicit reference time to `Parse`:

```csharp
// Explicit reference time — deterministic
var expr = CronexExpression.Parse("@once +20m", referenceTime: DateTimeOffset.UtcNow);
expr.OnceAt  // → referenceTime + 20 minutes as an absolute instant

// No reference time — uses UTC Now at the moment of Parse call
var expr = CronexExpression.Parse("@once +20m");
```

After resolution, it behaves identically to an absolute `@once`. `ToString()` returns the resolved absolute time.

### 3.8 Options Block `{...}`

The options block appears after the schedule body, separated by whitespace.
It is enclosed in `{}` and contains comma-separated key:value pairs.

| Option | Type | Meaning | Default |
|--------|------|---------|---------|
| `jitter` | duration | Adds a random delay in `[0, jitter)` to each execution (non-deterministic) | none (0) |
| `stagger` | duration | Fixed offset in `[0, stagger)` based on trigger ID (deterministic) | none (0) |
| `window` | duration | Execution is allowed only within the window after the scheduled time. Skipped if exceeded. | unlimited |
| `from` | date/datetime | Occurrences before this time are ignored | none |
| `until` | date/datetime | Occurrences after this time are ignored | none |
| `max` | integer | Total execution count limit. No more occurrences after reaching max. | unlimited |
| `tag` | string | Metadata tag. No effect on execution logic. Multiple tags joined with `+`. | none |

**`from`/`until` with date-only values:**
- `from:2025-06-01` → `2025-06-01T00:00:00` (midnight in the applicable timezone)
- `until:2025-12-31` → `2025-12-31T23:59:59.999` (last moment of the day in the applicable timezone)

**Difference between `jitter` and `stagger`:**

| | `jitter` | `stagger` |
|-|----------|-----------|
| Nature | Non-deterministic (random each execution) | Deterministic (fixed at registration) |
| Offset calculation | New random value in `[0, jitter)` each time | `hash(triggerId) % stagger` — constant |
| Use case | Load spreading (e.g. external API calls) | Collision avoidance (e.g. top-of-hour stampedes) |
| Predictability | Unpredictable | Same trigger ID always yields the same offset |

Both options can be used together. Application order: `scheduled_time + stagger_offset + jitter_random`.

**Interaction of `jitter`/`stagger` with `window`:**
If the time after applying jitter + stagger exceeds the window, the occurrence is skipped.

**`max` counting:**
Skipped occurrences are not counted. Only actually executed (or attempted) invocations count toward max.

### 3.9 Alias Expansion

| Alias | Equivalent cron |
|-------|-----------------|
| `@yearly`, `@annually` | `0 0 1 1 *` |
| `@monthly` | `0 0 1 * *` |
| `@weekly` | `0 0 * * 0` |
| `@daily`, `@midnight` | `0 0 * * *` |
| `@hourly` | `0 * * * *` |

Aliases can be combined with a TZ prefix and options block:
`TZ=Asia/Seoul @daily {jitter:5m}`

---

## 4. Expression Examples

### Basic (standard cron compatible)

```
*/5 * * * *                              # every 5 minutes
0 9 * * MON-FRI                          # weekdays at 09:00
0 0 1 * *                                # first day of each month at midnight
30 4 1,15 * *                            # 1st and 15th of each month at 04:30
0 22 * * 1-5                             # weekdays at 22:00
```

### 6-field (with seconds)

```
0 */5 * * * *                            # every 5 minutes at 0 seconds
30 0 * * * *                             # every hour at 0 minutes 30 seconds
*/10 * * * * *                           # every 10 seconds
```

### Timezone

```
TZ=Asia/Seoul 0 9 * * *                  # daily at 09:00 Seoul time
TZ=America/New_York 0 17 * * MON-FRI     # weekdays at 17:00 New York time
TZ=UTC 0 0 * * *                         # midnight UTC
```

### Extended characters

```
0 0 L * *                                # last day of each month at midnight
0 0 LW * *                               # nearest weekday to the last day of month
0 0 L-3 * *                              # 3 days before the last day of month
0 0 15W * *                              # nearest weekday to the 15th
0 0 * * 5L                               # last Friday of each month
0 0 * * MON#2                            # second Monday of each month
```

### Interval

```
@every 30m                               # every 30 minutes
@every 2h                                # every 2 hours
@every 1h30m                             # every 1 hour 30 minutes
@every 45s                               # every 45 seconds
@every 1h-2h                             # random interval between 1 and 2 hours
```

### One-shot

```
@once 2025-03-01T09:00:00+09:00          # specific time, once (absolute)
@once 2025-12-31T23:59:59Z               # UTC-based (absolute)
@once +20m                               # 20 minutes from now (relative)
@once +1h30m                             # 1 hour 30 minutes from now (relative)
```

### Alias

```
@daily                                   # daily at midnight
@hourly                                  # every hour on the hour
@weekly                                  # every Sunday at midnight
```

### Options

```
0 9 * * * {jitter:30s}                   # daily at 09:00 + random 0–30s (non-deterministic)
0 * * * * {stagger:5m}                   # every hour + fixed offset 0–5m (deterministic)
0 9 * * * {stagger:3m, jitter:10s}       # stagger + jitter combined
0 9 * * * {window:15m}                   # execute only within 09:00–09:15
*/10 * * * * {until:2025-12-31}          # until end of 2025
@every 1h {max:10}                       # max 10 executions
@every 5m {from:2025-06-01, until:2025-12-31}  # bounded time range
```

### Combined

```
TZ=Asia/Seoul 0 9 * * MON-FRI {jitter:30s, until:2025-12-31}
TZ=UTC @every 15m {window:5m, max:100, tag:health-check}
TZ=America/Chicago 0 0 L * * {jitter:1m, tag:monthly+report}
0 * * * * {stagger:5m, tag:hourly+batch}
@once 2025-04-01T02:00:00+09:00 {tag:migration}
@once +30m {tag:reminder}
```

---

## 5. Validation Rules

The parser applies the following rules and returns a structured list of errors on violation.

### 5.1 Field Range Validation

| Rule ID | Validation | Error message |
|---------|------------|---------------|
| `E001` | second ∈ [0, 59] | `second: value {v} out of range [0, 59]` |
| `E002` | minute ∈ [0, 59] | `minute: value {v} out of range [0, 59]` |
| `E003` | hour ∈ [0, 23] | `hour: value {v} out of range [0, 23]` |
| `E004` | day of month ∈ [1, 31] | `dayOfMonth: value {v} out of range [1, 31]` |
| `E005` | month ∈ [1, 12] | `month: value {v} out of range [1, 12]` |
| `E006` | day of week ∈ [0, 7] | `dayOfWeek: value {v} out of range [0, 7]` |
| `E007` | step > 0 | `{field}: step must be positive, got {v}` |

### 5.2 Structural Validation

| Rule ID | Validation | Error message |
|---------|------------|---------------|
| `E010` | Field count is 5 or 6 (cron) | `expected 5 or 6 fields, got {n}` |
| `E011` | TZ is a valid IANA timezone | `timezone: unknown timezone '{tz}'` |
| `E012` | @once datetime is valid ISO 8601 | `once: invalid datetime format '{v}'` |
| `E013` | @every duration is positive | `every: duration must be positive` |
| `E014` | @every range: min < max | `every: min duration must be less than max` |
| `E015` | Unknown option key | `options: unknown option '{key}'` |
| `E016` | Option value type mismatch | `options.{key}: expected {type}, got '{v}'` |
| `E017` | @once relative duration is positive | `once: relative duration must be positive` |

### 5.3 Logical Validation

| Rule ID | Validation | Error message |
|---------|------------|---------------|
| `E020` | from < until | `options: 'from' must be before 'until'` |
| `E021` | max > 0 | `options.max: must be positive, got {v}` |
| `E022` | jitter < 50% of schedule min interval | `options.jitter: {v} exceeds 50% of schedule interval` (warning) |
| `E023` | window > 0 | `options.window: must be positive` |
| `E024` | stagger > 0 | `options.stagger: must be positive` |
| `E025` | stagger < schedule min interval | `options.stagger: {v} exceeds schedule interval` (warning) |

### 5.4 Warning Codes

Warnings are non-blocking messages that flag potential issues in otherwise valid expressions.

| Rule ID | Validation | Warning message |
|---------|------------|-----------------|
| `E022` | jitter < 50% of schedule min interval | `options.jitter: {v} exceeds 50% of schedule interval` |
| `E025` | stagger < schedule min interval | `options.stagger: {v} exceeds schedule interval` |
| `W001` | Duplicate tag value (e.g. `tag:foo+bar+foo`) | `duplicate tag '{tag}'` |

**Implementation note:** E022/E025 compare against the interval directly extracted from `@every` expressions.
For cron expressions, computing the minimum interval requires field-combination analysis and is currently not applied.

### 5.5 Validation Result Structure

```
ValidationResult:
  IsValid: bool
  Errors: ValidationError[]
  Warnings: ValidationWarning[]

ValidationError:
  Code: string         # "E001"
  Field: string        # "hour"
  Message: string      # "value 25 out of range [0, 23]"
  Value: string        # "25"
  Position: int?       # character position in the expression (optional)

ValidationWarning:
  Code: string
  Field: string
  Message: string
```

---

## 6. Serialization / Deserialization

### 6.1 Canonical Form

`ToString()` produces a canonical string in this order:

```
[TZ=<timezone> ]<schedule>[ {<options>}]
```

- If a timezone is set, the string begins with a `TZ=` prefix.
- Options are sorted alphabetically by key.
- Durations use the largest unit first (e.g. `1h30m`, not `90m`).
- Extraneous whitespace is removed.

### 6.2 Equivalence

Two expressions are **semantically equivalent** if:
- Their canonical schedule bodies are identical.
- Their timezones are identical (absent = absent).
- All option key-value pairs are identical.

`0 0 * * 0` and `@weekly` are semantically equivalent.
`0 0 * * 7` and `0 0 * * 0` are semantically equivalent (both represent Sunday).

---

## 7. JSON Schema Reference

### 7.1 TriggerDefinition Schema

`TriggerDefinition` is a serializable trigger specification. It contains no runtime handler (delegate),
enabling external systems (CLIs, APIs, config files) to produce definitions that consuming apps bind to handlers.

```json
{
  "$schema": "http://json-schema.org/draft-07/schema#",
  "title": "Cronex TriggerDefinition",
  "type": "object",
  "required": ["id", "expression"],
  "properties": {
    "id": {
      "type": "string",
      "pattern": "^[a-zA-Z0-9_-]+$",
      "description": "Unique trigger identifier"
    },
    "expression": {
      "type": "string",
      "description": "Cronex expression string",
      "examples": [
        "*/5 * * * *",
        "TZ=Asia/Seoul 0 9 * * MON-FRI {jitter:30s}",
        "0 * * * * {stagger:5m}",
        "@every 15m {max:100}",
        "@once 2025-03-01T09:00:00+09:00",
        "@once +20m"
      ]
    },
    "enabled": {
      "type": "boolean",
      "default": true
    },
    "metadata": {
      "type": "object",
      "additionalProperties": { "type": "string" },
      "description": "Free-form key-value pairs passed through to TriggerContext"
    }
  }
}
```

### 7.2 TriggerDefinition Usage Pattern

**External system produces a trigger definition as JSON:**

```json
{
  "id": "health-check",
  "expression": "TZ=UTC @every 15m {stagger:3m}",
  "enabled": true,
  "metadata": {
    "endpoint": "https://api.example.com/health",
    "env": "prod",
    "delivery.mode": "webhook",
    "delivery.to": "https://hooks.example.com/results"
  }
}
```

**Consuming app binds a handler:**

```csharp
var definition = JsonSerializer.Deserialize<TriggerDefinition>(json);
scheduler.Register(definition, async (ctx, ct) =>
{
    // ctx.Metadata contains all keys from the definition
    var endpoint = ctx.Metadata["endpoint"];
    await HealthCheckAsync(endpoint, ct);
});
```

What Cronex guarantees in this pattern:
1. The expression is a single string — easy to generate and validate programmatically.
2. `Validate()` returns structured errors — callers can self-correct invalid expressions.
3. Metadata is free-form — the consuming app can carry any context it needs.

### 7.3 Metadata Conventions

Cronex does not interpret metadata keys. The following are recommended conventions for interoperability between consuming apps:

| Key | Purpose | Example |
|-----|---------|---------|
| `env` | Environment tag | `"prod"`, `"staging"` |
| `endpoint` | Target API endpoint | `"https://api.example.com"` |
| `scope` | Execution isolation hint | `"isolated"`, `"shared"` |
| `scope.session` | Session key | `"cron:health-check"` |
| `delivery.mode` | Result delivery mode | `"webhook"`, `"queue"`, `"none"` |
| `delivery.to` | Result destination | `"https://hooks.example.com/results"` |
| `delivery.channel` | Notification channel | `"slack"`, `"email"` |

These are conventions, not enforcement. Consuming apps are free to define their own keys.

### 7.4 Persistence Boundary

Cronex owns the in-process trigger registry only. It does not persist triggers across restarts, and it
never will — that would pull a database or file store into a zero-dependency library.

`TriggerDefinition` (§7.1) is the designed extension point for persistence: it carries everything needed
to store and rehydrate a trigger — `id`, `expression`, `enabled`, `metadata` — without the runtime handler
(delegate), which cannot be serialized and is not Cronex's concern.

What Cronex guarantees:
1. `TriggerDefinition` round-trips through JSON without loss (§7.1 schema).
2. `Validate()` can check a definition's expression before it is persisted, so invalid schedules never
   reach storage.
3. Re-registering a definition after a restart reconstructs an equivalent schedule (`NextFireTime`
   recomputed from the expression).

What Cronex leaves to the host application:
1. **Where** definitions are stored (file, database, queue — Cronex has no opinion).
2. **When** to snapshot the current registry and when to reload it (no built-in export/import API).
3. **Execution history across restarts** — `FireCount` and `LastFired` live on the in-memory
   `TriggerRegistration` and reset when a definition is re-registered. If a host needs `{max:N}` to survive
   a restart, it must track fire counts itself and stop re-registering once the limit is reached.
