using System.Reflection;
using Iyu.Conventions.Testing.Snippets;
using Shouldly;
using Xunit;

namespace Cronex.Tests;

/// <summary>
/// Compiles every C# block in README.md against the current assemblies — what a reader who copies a block runs into: the
/// receiver, the arguments, the members read and the namespaces.
/// </summary>
/// <remarks>
/// Each block is compiled as a top-level program by <see cref="SnippetCompiler"/>: its own usings, the common usings below,
/// and the stand-ins below when the block uses the name without declaring it — the reader's own values, helper methods and
/// types (a logger, the job the trigger runs, a leader lock), not part of what the block shows.
/// </remarks>
public class ReadmeSnippetCompileTests
{
    // A block that is deliberately not a program — a type's shape written as a summary — by the heading it sits under,
    // with the reason. Shrink this, never grow it silently.
    private static readonly SnippetRoster Fragments = new(new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["TriggerContext"] = "a summary of the context's members, not a declaration",
        ["TriggerDefinition (JSON-serializable)"] = "restates the shape of an existing type",
    });

    private static readonly string[] CommonUsings =
    [
        "System",
        "System.Collections.Generic",
        "System.Linq",
        "System.Text.Json",
        "System.Threading",
        "System.Threading.Tasks",
        "Cronex",
        "Cronex.Hosting",
        "Microsoft.Extensions.DependencyInjection",
        "Microsoft.Extensions.Hosting",
        "Microsoft.Extensions.Logging",
    ];

    private static readonly Dictionary<string, string> StandIns = new(StringComparer.Ordinal)
    {
        ["scheduler"] = "var scheduler = new CronexScheduler();",
        ["logger"] = "ILogger logger = null!;",
        ["input"] = "string input = \"0 9 * * *\";",
        ["definition"] = "var definition = new TriggerDefinition { Id = \"sync\", Expression = \"@every 15m\" };",
        // The reader's own leader lock; its type is a type stand-in below.
        ["leaderLock"] = "LeaderLock leaderLock = new();",
        ["builder"] = "IHostApplicationBuilder builder = null!;",

        // The reader's own helper methods.
        ["GenerateReport"] = "static Task GenerateReport(DateTimeOffset at, CancellationToken ct) => Task.CompletedTask;",
        ["GenerateReportAsync"] = "static Task GenerateReportAsync(TriggerContext ctx, CancellationToken ct) => Task.CompletedTask;",
        ["CleanupAsync"] = "static Task CleanupAsync(CancellationToken ct) => Task.CompletedTask;",
        ["SyncAsync"] = "static Task<string> SyncAsync(string endpoint, CancellationToken ct) => Task.FromResult(\"\");",
        ["HttpPost"] = "static Task HttpPost(string url, string body, CancellationToken ct) => Task.CompletedTask;",
        ["IsCurrentLeaderAsync"] = "static Task<bool> IsCurrentLeaderAsync(string? session) => Task.FromResult(true);",

        // The reader's own types, declared after the program.
        ["IReportService"] = "public interface IReportService { Task GenerateAsync(DateTimeOffset at, CancellationToken ct); }",
        ["LeaderLock"] = "sealed class LeaderLock { public Task<bool> TryAcquireAsync() => Task.FromResult(true); }",
    };

    private static readonly string[] AssembliesToLoad =
    [
        "Cronex", "Cronex.Hosting", "Microsoft.Extensions.Hosting",
        "Microsoft.Extensions.DependencyInjection.Abstractions", "Microsoft.Extensions.Hosting.Abstractions",
        "Microsoft.Extensions.Logging.Abstractions",
    ];

    private static readonly SnippetCompiler Compiler = new(SnippetCompiler.ReferencesFor([.. AssembliesToLoad.Select(Assembly.Load)]))
    {
        Usings = CommonUsings,
        StandIns = StandIns,
    };

    private static readonly IReadOnlyList<DocSnippet> Snippets = DocSnippets.Read(ReadmePath(), "README.md");

    public static TheoryData<string> Blocks() => new(Snippets.Select(s => s.Key));

    [Theory]
    [MemberData(nameof(Blocks))]
    public void ReadmeBlock_Compiles(string key)
    {
        var snippet = Snippets.Single(s => s.Key == key);
        if (!Fragments.IsFragment(snippet))
            Compiler.ShouldCompile(snippet);
    }

    [Fact]
    public void EveryReadmeBlock_IsFoundAndFragmentsNameRealHeadings() => Fragments.ShouldMatch([.. Snippets], atLeast: 13);

    /// <summary>Positive control: the compiler rejects a block that declares one name twice, as the old Parse block did.</summary>
    [Fact]
    public void Compile_RejectsARedeclaredVariable()
    {
        var errors = Compiler.Compile("""
            var expr = CronexExpression.Parse("0 9 * * *");
            if (CronexExpression.TryParse(input, out var expr, out var error))
                Console.WriteLine(expr.GetNextOccurrence(DateTimeOffset.UtcNow));
            """);

        errors.ShouldNotBeEmpty();
    }

    private static string ReadmePath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Cronex.slnx")))
            dir = dir.Parent;
        return Path.Combine(
            dir?.FullName ?? throw new InvalidOperationException("Cronex.slnx not found above the test output directory"),
            "README.md");
    }
}
