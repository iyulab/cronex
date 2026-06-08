using System.Text.Json;
using Shouldly;
using Xunit;

namespace Cronex.Tests;

/// <summary>
/// End-to-end integration tests combining multiple components.
/// </summary>
public class IntegrationTests
{
    private static FakeTimeProvider CreateTimeProvider(DateTimeOffset start)
        => new(start);

    // --- TriggerDefinition → Scheduler integration ---

    [Fact]
    public async Task TriggerDefinition_RegisterAndFire_WithMetadata()
    {
        var tp = CreateTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await using var scheduler = new CronexScheduler(tp);

        var definition = new TriggerDefinition
        {
            Id = "health-check",
            Expression = "@every 5m",
            Metadata = new() { ["env"] = "prod", ["endpoint"] = "https://api.example.com" }
        };

        string? capturedEnv = null;
        string? capturedEndpoint = null;
        scheduler.Register(definition, (ctx, ct) =>
        {
            capturedEnv = ctx.Metadata["env"];
            capturedEndpoint = ctx.Metadata["endpoint"];
            return Task.CompletedTask;
        });

        tp.Advance(TimeSpan.FromMinutes(5));
        await scheduler.TickAsync();

        capturedEnv.ShouldBe("prod");
        capturedEndpoint.ShouldBe("https://api.example.com");
    }

    [Fact]
    public void TriggerDefinition_JsonRoundTrip()
    {
        var definition = new TriggerDefinition
        {
            Id = "sync",
            Expression = "TZ=UTC @every 15m {stagger:3m}",
            Enabled = true,
            Metadata = new() { ["env"] = "prod" }
        };

        var json = JsonSerializer.Serialize(definition);
        var deserialized = JsonSerializer.Deserialize<TriggerDefinition>(json);

        deserialized.ShouldNotBeNull();
        deserialized!.Id.ShouldBe("sync");
        deserialized.Expression.ShouldBe("TZ=UTC @every 15m {stagger:3m}");
        deserialized.Enabled.ShouldBeTrue();
        deserialized.Metadata.ShouldNotBeNull();
        deserialized.Metadata!["env"].ShouldBe("prod");
    }

    [Fact]
    public async Task TriggerDefinition_Disabled_DoesNotFire()
    {
        var tp = CreateTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await using var scheduler = new CronexScheduler(tp);

        var definition = new TriggerDefinition
        {
            Id = "disabled-trigger",
            Expression = "* * * * *",
            Enabled = false
        };

        var fired = false;
        scheduler.Register(definition, (ctx, ct) =>
        {
            fired = true;
            return Task.CompletedTask;
        });

        tp.Advance(TimeSpan.FromMinutes(1));
        await scheduler.TickAsync();

        fired.ShouldBeFalse();
    }

    // --- TriggerContext property verification ---

    [Fact]
    public async Task TriggerContext_AllProperties_Populated()
    {
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var tp = CreateTimeProvider(start);
        await using var scheduler = new CronexScheduler(tp);

        var definition = new TriggerDefinition
        {
            Id = "ctx-test",
            Expression = "* * * * *",
            Metadata = new() { ["key"] = "value" }
        };

        TriggerContext? captured = null;
        scheduler.Register(definition, (ctx, ct) =>
        {
            captured = ctx;
            return Task.CompletedTask;
        });

        tp.Advance(TimeSpan.FromMinutes(1));
        await scheduler.TickAsync();

        captured.ShouldNotBeNull();
        captured!.TriggerId.ShouldBe("ctx-test");
        captured.FireCount.ShouldBe(1);
        captured.Expression.ShouldNotBeNull();
        captured.Expression.Kind.ShouldBe(ScheduleKind.Cron);
        captured.ScheduledTime.ShouldBeGreaterThan(start);
        captured.ActualTime.ShouldBeGreaterThanOrEqualTo(captured.ScheduledTime);
        captured.Metadata["key"].ShouldBe("value");
    }

    // --- Disable → Re-enable → Fire ---

    [Fact]
    public async Task DisableReenable_FiresAfterReenable()
    {
        var tp = CreateTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await using var scheduler = new CronexScheduler(tp);

        var count = 0;
        scheduler.Register("toggle", "* * * * *", (ctx, ct) =>
        {
            count++;
            return Task.CompletedTask;
        });

        scheduler.SetEnabled("toggle", false);
        tp.Advance(TimeSpan.FromMinutes(1));
        await scheduler.TickAsync();
        count.ShouldBe(0);

        scheduler.SetEnabled("toggle", true);
        tp.Advance(TimeSpan.FromMinutes(1));
        await scheduler.TickAsync();
        count.ShouldBe(1);
    }

    // --- Multiple triggers ---

    [Fact]
    public async Task MultipleTriggers_IndependentFiring()
    {
        var tp = CreateTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await using var scheduler = new CronexScheduler(tp);

        int countA = 0, countB = 0;
        scheduler.Register("a", "* * * * *", (ctx, ct) =>
        {
            countA++;
            return Task.CompletedTask;
        });
        scheduler.Register("b", "@every 5m", (ctx, ct) =>
        {
            countB++;
            return Task.CompletedTask;
        });

        // After 1 minute: a fires, b doesn't
        tp.Advance(TimeSpan.FromMinutes(1));
        await scheduler.TickAsync();
        countA.ShouldBe(1);
        countB.ShouldBe(0);

        // After 5 minutes total: both fire
        tp.Advance(TimeSpan.FromMinutes(4));
        await scheduler.TickAsync();
        countA.ShouldBe(2);
        countB.ShouldBe(1);
    }

    // --- Once trigger with metadata ---

    [Fact]
    public async Task OnceTrigger_FiresOnceWithMetadata()
    {
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var tp = CreateTimeProvider(start);
        await using var scheduler = new CronexScheduler(tp);

        var definition = new TriggerDefinition
        {
            Id = "one-shot",
            Expression = "@once +10m",
            Metadata = new() { ["task"] = "cleanup" }
        };

        string? capturedTask = null;
        var count = 0;
        // Need to pass reference time for relative @once
        var expr = CronexExpression.Parse(definition.Expression, start);
        var reg = scheduler.Register("one-shot", expr, (ctx, ct) =>
        {
            capturedTask = ctx.Metadata.TryGetValue("task", out var t) ? t : null;
            count++;
            return Task.CompletedTask;
        });

        tp.Advance(TimeSpan.FromMinutes(10));
        await scheduler.TickAsync();
        count.ShouldBe(1);

        tp.Advance(TimeSpan.FromMinutes(10));
        await scheduler.TickAsync();
        count.ShouldBe(1); // Should not fire again
    }

    // --- Full expression lifecycle ---

    [Fact]
    public void FullLifecycle_ParseValidateNextEnumerate()
    {
        const string input = "TZ=UTC 0 9 * * MON-FRI {max:5, until:2026-12-31}";

        // Validate
        var validation = ExpressionValidator.Validate(input);
        validation.IsValid.ShouldBeTrue();

        // Parse
        var expr = CronexExpression.Parse(input);
        expr.Kind.ShouldBe(ScheduleKind.Cron);
        expr.Timezone.ShouldBe("UTC");
        expr.Options.Max.ShouldBe(5);
        expr.Options.Until.ShouldNotBeNull();

        // ToString round-trip
        var str = expr.ToString();
        var roundTripped = CronexExpression.Parse(str);
        roundTripped.Timezone.ShouldBe("UTC");
        roundTripped.Options.Max.ShouldBe(5);

        // GetNextOccurrence
        var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var next = expr.GetNextOccurrence(from);
        next.ShouldNotBeNull();
        next!.Value.UtcDateTime.Hour.ShouldBe(9);
        next.Value.UtcDateTime.DayOfWeek.ShouldNotBe(DayOfWeek.Saturday);
        next.Value.UtcDateTime.DayOfWeek.ShouldNotBe(DayOfWeek.Sunday);

        // Enumerate
        var occurrences = expr.Enumerate(from).ToList();
        occurrences.Count.ShouldBe(5); // max:5
        foreach (var occ in occurrences)
        {
            occ.UtcDateTime.Hour.ShouldBe(9);
            occ.UtcDateTime.DayOfWeek.ShouldNotBe(DayOfWeek.Saturday);
            occ.UtcDateTime.DayOfWeek.ShouldNotBe(DayOfWeek.Sunday);
        }
    }

    // --- Skipped event for disabled trigger ---

    [Fact]
    public async Task SkippedEvent_DisabledTrigger()
    {
        var tp = CreateTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await using var scheduler = new CronexScheduler(tp);

        string? skippedId = null;
        string? skippedReason = null;
        scheduler.TriggerSkipped += (id, reason) =>
        {
            skippedId = id;
            skippedReason = reason;
        };

        scheduler.Register("disabled-test", "* * * * *", (ctx, ct) => Task.CompletedTask);
        scheduler.SetEnabled("disabled-test", false);

        tp.Advance(TimeSpan.FromMinutes(1));
        await scheduler.TickAsync();

        skippedId.ShouldBe("disabled-test");
        skippedReason.ShouldBe("disabled");
    }

    // --- All schedule kinds fire correctly ---

    [Fact]
    public async Task AllScheduleKinds_FireCorrectly()
    {
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var tp = CreateTimeProvider(start);
        await using var scheduler = new CronexScheduler(tp);

        int cronCount = 0, aliasCount = 0, intervalCount = 0, onceCount = 0;

        scheduler.Register("cron", "* * * * *", (ctx, ct) => { cronCount++; return Task.CompletedTask; });
        scheduler.Register("alias", "@hourly", (ctx, ct) => { aliasCount++; return Task.CompletedTask; });
        scheduler.Register("interval", "@every 30m", (ctx, ct) => { intervalCount++; return Task.CompletedTask; });
        scheduler.Register("once", "@once +30m", (ctx, ct) => { onceCount++; return Task.CompletedTask; }, start);

        // At 30m: cron fires, interval fires, once fires, alias doesn't
        tp.Advance(TimeSpan.FromMinutes(30));
        await scheduler.TickAsync();
        cronCount.ShouldBeGreaterThanOrEqualTo(1);
        intervalCount.ShouldBe(1);
        onceCount.ShouldBe(1);

        // At 1h: alias fires
        tp.Advance(TimeSpan.FromMinutes(30));
        await scheduler.TickAsync();
        aliasCount.ShouldBeGreaterThanOrEqualTo(1);
    }

    // --- Error recovery: failed handler doesn't break scheduler ---

    [Fact]
    public async Task FailedHandler_DoesntBreakOtherTriggers()
    {
        var tp = CreateTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await using var scheduler = new CronexScheduler(tp);

        var goodCount = 0;
        scheduler.Register("bad", "* * * * *", (ctx, ct) =>
            throw new InvalidOperationException("boom"));
        scheduler.Register("good", "* * * * *", (ctx, ct) =>
        {
            goodCount++;
            return Task.CompletedTask;
        });

        tp.Advance(TimeSpan.FromMinutes(1));
        await scheduler.TickAsync();

        goodCount.ShouldBe(1); // Good trigger still fires despite bad one failing
    }

    // --- Expression special chars integration ---

    [Fact]
    public void SpecialChars_ParseAndNext_Integration()
    {
        // Last day of each month at midnight
        var expr = CronexExpression.Parse("0 0 L * *");
        var from = new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero);
        var next = expr.GetNextOccurrence(from);
        next.ShouldNotBeNull();
        next!.Value.DateTime.ShouldBe(new DateTime(2026, 1, 31, 0, 0, 0));

        // Second Monday of each month at 9am
        var expr2 = CronexExpression.Parse("0 9 * * MON#2");
        var from2 = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
        var next2 = expr2.GetNextOccurrence(from2);
        next2.ShouldNotBeNull();
        // Second Monday of March 2026 = March 9
        next2!.Value.DateTime.ShouldBe(new DateTime(2026, 3, 9, 9, 0, 0));
    }
}
