using System.Collections.Immutable;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Shouldly;
using Xunit;

namespace Cronex.Tests;

/// <summary>
/// Compiles every <c>```csharp</c> block in README.md against the current assemblies — what a reader who copies a block
/// runs into: the receiver, the arguments, the members read and the namespaces.
/// </summary>
/// <remarks>
/// A block is compiled as a top-level program: its <c>using</c> lines are hoisted, the common usings below are added, and
/// the stand-ins below are declared when the block uses the name without declaring it — the reader's own values, helper
/// methods and types (a logger, the job the trigger runs, a leader lock), not part of what the block shows.
/// </remarks>
public class ReadmeSnippetCompileTests
{
    // A block that is deliberately not a program — a type's shape written as a summary — is recognized by a marker in
    // its text, with the reason. Shrink this, never grow it silently.
    private static readonly Dictionary<string, string> Fragments = new(StringComparer.Ordinal)
    {
        ["public sealed class TriggerContext"] = "a summary of the context's members, not a declaration",
        ["public sealed class TriggerDefinition"] = "restates the shape of an existing type",
    };

    private const string CommonUsings = """
        using System;
        using System.Collections.Generic;
        using System.Linq;
        using System.Text.Json;
        using System.Threading;
        using System.Threading.Tasks;
        using Cronex;
        using Cronex.Hosting;
        using Microsoft.Extensions.DependencyInjection;
        using Microsoft.Extensions.Hosting;
        using Microsoft.Extensions.Logging;
        """;

    private static readonly (string Name, string Declaration)[] StandIns =
    [
        ("scheduler", "var scheduler = new CronexScheduler();"),
        ("logger", "ILogger logger = null!;"),
        ("input", "string input = \"0 9 * * *\";"),
        ("definition", "var definition = new TriggerDefinition { Id = \"sync\", Expression = \"@every 15m\" };"),
        ("leaderLock", "LeaderLock leaderLock = new();"),
        ("builder", "IHostApplicationBuilder builder = null!;"),
    ];

    private static readonly (string Name, string Declaration)[] StandInFunctions =
    [
        ("GenerateReport", "static Task GenerateReport(DateTimeOffset at, CancellationToken ct) => Task.CompletedTask;"),
        ("GenerateReportAsync", "static Task GenerateReportAsync(TriggerContext ctx, CancellationToken ct) => Task.CompletedTask;"),
        ("CleanupAsync", "static Task CleanupAsync(CancellationToken ct) => Task.CompletedTask;"),
        ("SyncAsync", "static Task<string> SyncAsync(string endpoint, CancellationToken ct) => Task.FromResult(\"\");"),
        ("HttpPost", "static Task HttpPost(string url, string body, CancellationToken ct) => Task.CompletedTask;"),
        ("IsCurrentLeaderAsync", "static Task<bool> IsCurrentLeaderAsync(string? session) => Task.FromResult(true);"),
    ];

    private static readonly (string Name, string Declaration)[] StandInTypes =
    [
        ("LeaderLock", "sealed class LeaderLock { public Task<bool> TryAcquireAsync() => Task.FromResult(true); }"),
        ("IReportService", "public interface IReportService { Task GenerateAsync(DateTimeOffset at, CancellationToken ct); }"),
    ];

    private static readonly string[] AssembliesToLoad =
    [
        "Cronex", "Cronex.Hosting", "Microsoft.Extensions.Hosting",
        "Microsoft.Extensions.DependencyInjection.Abstractions", "Microsoft.Extensions.Hosting.Abstractions",
        "Microsoft.Extensions.Logging.Abstractions",
    ];

    public static TheoryData<string> Blocks()
    {
        var data = new TheoryData<string>();
        foreach (var block in ReadBlocks())
            data.Add(block.Key);
        return data;
    }

    [Theory]
    [MemberData(nameof(Blocks))]
    public void ReadmeBlock_Compiles(string key)
    {
        var block = ReadBlocks().Single(b => b.Key == key);
        if (Fragments.Keys.Any(marker => block.Code.Contains(marker, StringComparison.Ordinal)))
            return;

        var errors = Compile(block.Code);

        errors.ShouldBeEmpty(
            $"README block {key} does not compile against the current API:\n" +
            string.Join("\n", errors.Select(e => e.ToString())) + "\n--- source ---\n" + Program(block.Code));
    }

    [Fact]
    public void EveryReadmeBlock_IsFoundAndEveryFragmentMarkerIsUsed()
    {
        var blocks = ReadBlocks();
        blocks.Count.ShouldBeGreaterThanOrEqualTo(13);
        foreach (var marker in Fragments.Keys)
            blocks.ShouldContain(b => b.Code.Contains(marker, StringComparison.Ordinal));
    }

    /// <summary>Positive control: the compiler rejects a block that declares one name twice, as the old Parse block did.</summary>
    [Fact]
    public void Compile_RejectsARedeclaredVariable()
    {
        var errors = Compile("""
            var expr = CronexExpression.Parse("0 9 * * *");
            if (CronexExpression.TryParse(input, out var expr, out var error))
                Console.WriteLine(expr.GetNextOccurrence(DateTimeOffset.UtcNow));
            """);

        errors.ShouldNotBeEmpty();
    }

    private sealed record Block(string Key, string Heading, string Code);

    private static List<Block> ReadBlocks()
    {
        var lines = File.ReadAllText(ReadmePath()).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var blocks = new List<Block>();
        var heading = "(top)";
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].StartsWith('#'))
                heading = lines[i].TrimStart('#').Trim();
            if (lines[i].Trim() != "```csharp")
                continue;

            var start = i + 1;
            var code = new StringBuilder();
            for (i++; i < lines.Length && lines[i].Trim() != "```"; i++)
                code.AppendLine(lines[i]);
            blocks.Add(new Block($"line {start}: {heading}", heading, code.ToString()));
        }

        return blocks;
    }

    private static string Program(string code)
    {
        var lines = code.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        bool IsUsingDirective(string l)
        {
            var t = l.Trim();
            return t.StartsWith("using ", StringComparison.Ordinal) && t.EndsWith(';') && !t.StartsWith("using var ", StringComparison.Ordinal);
        }

        var body = string.Join("\n", lines.Where(l => !IsUsingDirective(l)));
        bool Uses(string name) => Regex.IsMatch(body, $@"\b{name}\b");

        var standIns = StandIns
            .Where(s => Uses(s.Name) && !Regex.IsMatch(body, $@"\b(var|[A-Z][\w<>?,\s]*)\s+{s.Name}\s*[=;]"))
            .Select(s => s.Declaration)
            .ToList();
        var functions = StandInFunctions.Where(f => Uses(f.Name)).Select(f => f.Declaration);
        // A stand-in value can name a stand-in type (leaderLock is a LeaderLock), so types are chosen from both.
        var declared = body + "\n" + string.Join("\n", standIns);
        var types = StandInTypes.Where(t => Regex.IsMatch(declared, $@"\b{t.Name}\b")).Select(t => t.Declaration);

        // Top-level statements (and the stand-in local functions) must come before the block's own type declarations.
        var typeStart = Regex.Match(body, @"^(public |internal )?(sealed )?(class|record|interface) ", RegexOptions.Multiline);
        var statements = typeStart.Success ? body[..typeStart.Index] : body;
        var declaredTypes = typeStart.Success ? body[typeStart.Index..] : "";

        return string.Join("\n", lines.Where(IsUsingDirective).Select(l => l.Trim())) + "\n" + CommonUsings + "\n"
               + string.Join("\n", standIns) + "\n" + statements + "\n" + string.Join("\n", functions) + "\n"
               + declaredTypes + "\n" + string.Join("\n", types);
    }

    private static ImmutableArray<Diagnostic> Compile(string code)
    {
        var tree = CSharpSyntaxTree.ParseText(Program(code), new CSharpParseOptions(LanguageVersion.Latest));
        var compilation = CSharpCompilation.Create(
            "ReadmeSnippet", [tree], References(),
            new CSharpCompilationOptions(OutputKind.ConsoleApplication, nullableContextOptions: NullableContextOptions.Enable));
        return compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToImmutableArray();
    }

    private static List<MetadataReference> References()
    {
        foreach (var name in AssembliesToLoad)
            Assembly.Load(name);

        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string trusted)
            paths.UnionWith(trusted.Split(Path.PathSeparator).Where(p => p.Length > 0));
        paths.UnionWith(AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && a.Location.Length > 0)
            .Select(a => a.Location));
        return paths.Select(p => (MetadataReference)MetadataReference.CreateFromFile(p)).ToList();
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
