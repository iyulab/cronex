using System.Text.Json;
using Shouldly;
using Xunit;

namespace Cronex.Tests;

public class TriggerDefinitionTests
{
    [Fact]
    public void Deserialize_Full()
    {
        var json = """
        {
            "id": "health-check",
            "expression": "TZ=UTC @every 15m {stagger:3m}",
            "enabled": true,
            "metadata": {
                "endpoint": "https://api.example.com/health",
                "env": "prod"
            }
        }
        """;

        var def = JsonSerializer.Deserialize<TriggerDefinition>(json);
        def.ShouldNotBeNull();
        def!.Id.ShouldBe("health-check");
        def.Expression.ShouldBe("TZ=UTC @every 15m {stagger:3m}");
        def.Enabled.ShouldBeTrue();
        def.Metadata.ShouldNotBeNull();
        def.Metadata!["endpoint"].ShouldBe("https://api.example.com/health");
        def.Metadata["env"].ShouldBe("prod");
    }

    [Fact]
    public void Deserialize_Minimal()
    {
        var json = """
        {
            "id": "test",
            "expression": "@daily"
        }
        """;

        var def = JsonSerializer.Deserialize<TriggerDefinition>(json);
        def.ShouldNotBeNull();
        def!.Id.ShouldBe("test");
        def.Enabled.ShouldBeTrue(); // default
        def.Metadata.ShouldBeNull();
    }

    [Fact]
    public void Deserialize_Disabled()
    {
        var json = """
        {
            "id": "test",
            "expression": "@daily",
            "enabled": false
        }
        """;

        var def = JsonSerializer.Deserialize<TriggerDefinition>(json);
        def!.Enabled.ShouldBeFalse();
    }

    [Fact]
    public void Serialize_RoundTrip()
    {
        var def = new TriggerDefinition
        {
            Id = "my-trigger",
            Expression = "0 9 * * MON-FRI",
            Metadata = new Dictionary<string, string>
            {
                ["scope"] = "production"
            }
        };

        var json = JsonSerializer.Serialize(def);
        var deserialized = JsonSerializer.Deserialize<TriggerDefinition>(json);
        deserialized.ShouldNotBeNull();
        deserialized!.Id.ShouldBe("my-trigger");
        deserialized.Expression.ShouldBe("0 9 * * MON-FRI");
        deserialized.Metadata!["scope"].ShouldBe("production");
    }

    // Integration: Register via TriggerDefinition

    [Fact]
    public async Task Register_WithDefinition()
    {
        var tp = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await using var scheduler = new CronexScheduler(tp);

        var def = new TriggerDefinition
        {
            Id = "test",
            Expression = "* * * * *",
            Metadata = new Dictionary<string, string>
            {
                ["key"] = "value"
            }
        };

        Dictionary<string, string>? capturedMetadata = null;
        scheduler.Register(def, (ctx, ct) =>
        {
            capturedMetadata = new Dictionary<string, string>(ctx.Metadata);
            return Task.CompletedTask;
        });

        tp.Advance(TimeSpan.FromMinutes(1));
        await scheduler.TickAsync();

        capturedMetadata.ShouldNotBeNull();
        capturedMetadata!["key"].ShouldBe("value");
    }

    [Fact]
    public async Task Register_WithDefinition_DisabledByDefault()
    {
        var tp = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await using var scheduler = new CronexScheduler(tp);

        var def = new TriggerDefinition
        {
            Id = "test",
            Expression = "* * * * *",
            Enabled = false
        };

        var fired = false;
        scheduler.Register(def, (ctx, ct) =>
        {
            fired = true;
            return Task.CompletedTask;
        });

        tp.Advance(TimeSpan.FromMinutes(1));
        await scheduler.TickAsync();

        fired.ShouldBeFalse();
    }

    [Fact]
    public void Register_WithDefinition_MetadataAvailableOnTrigger()
    {
        var scheduler = new CronexScheduler();

        var def = new TriggerDefinition
        {
            Id = "test",
            Expression = "* * * * *",
            Metadata = new Dictionary<string, string> { ["env"] = "test" }
        };

        var reg = scheduler.Register(def, (ctx, ct) => Task.CompletedTask);
        reg.Metadata.ShouldNotBeNull();
        reg.Metadata!["env"].ShouldBe("test");
    }
}
