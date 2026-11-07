using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.Fuzzing.Tests;

public sealed class FuzzTargetTests
{
    private static FullPath RepositoryRoot => FullPath.FromPath(AppContext.BaseDirectory).FindRequiredGitRepositoryRoot();

    public static TheoryData<string> Targets => [.. FuzzProjects.All.SelectMany(project => project.Targets).Select(target => target.Name)];

    [Theory]
    [MemberData(nameof(Targets))]
    public void SeedsAndRegressionsPass(string targetName)
    {
        var (project, target) = FuzzProjects.Get(targetName);
        var inputs = target.Seeds().Select(seed => (Name: "seed " + seed.Name, seed.Data)).ToList();
        Assert.NotEmpty(inputs);

        var regressions = RepositoryRoot / "fuzz" / project / "Regressions" / target.Name;
        if (Directory.Exists(regressions))
        {
            inputs.AddRange(Directory.EnumerateFiles(regressions).Order(StringComparer.Ordinal).Select(path => (Name: "regression " + Path.GetFileName(path), Data: (ReadOnlyMemory<byte>)File.ReadAllBytes(path))));
        }

        var failures = new List<string>();
        var stopwatch = Stopwatch.StartNew();
        foreach (var (name, data) in inputs)
        {
            try
            {
                target.Action(data.Span);
            }
            catch (Exception ex)
            {
                failures.Add($"{name}: {ex}");
            }
        }

        TestContext.Current.TestOutputHelper?.WriteLine($"{inputs.Count} inputs in {stopwatch.Elapsed.TotalSeconds:F1} s");
        if (failures.Count > 0)
            Assert.Fail($"{failures.Count}/{inputs.Count} inputs fail:{Environment.NewLine}{string.Join(Environment.NewLine + Environment.NewLine, failures)}");
    }

    [Fact]
    public void TargetNamesAreUnique()
    {
        var names = FuzzProjects.All.SelectMany(project => project.Targets).Select(target => target.Name).ToList();
        Assert.HasCount(names.Count, names.Distinct(StringComparer.Ordinal));
    }

    [Fact]
    public void DictionaryTokensAreNotEmpty()
    {
        // libFuzzer refuses a dictionary with an empty token ("ParseDictionaryFile: error in line N") and fuzzes nothing
        var targets = FuzzProjects.All.SelectMany(project => project.Targets).Where(target => target.Dictionary.Any(string.IsNullOrEmpty)).Select(target => target.Name).ToList();
        Assert.Empty(targets);
    }

    [Fact]
    public void EveryFuzzProjectIsTestedAndFuzzedWeekly()
    {
        var projects = Directory.EnumerateDirectories(RepositoryRoot / "fuzz", "*.Fuzz").Select(Path.GetFileName).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(projects, FuzzProjects.All.Select(project => project.Project).Order(StringComparer.Ordinal).ToList());

        // Each project is in the table of the SharpFuzz workflow, with the component label of its issues, which a manual run
        // can select
        var workflows = GetFuzzWorkflows();
        var sharpFuzz = workflows.Single(workflow => workflow.Name is SharpFuzzWorkflow).Text;
        var table = GetSharpFuzzProjects(sharpFuzz);
        Assert.Equal(projects, table.Select(project => project.Project).Order(StringComparer.Ordinal).ToList());
        Assert.Equal(["all", .. table.Select(project => project.Component)], Matches(sharpFuzz, @"^          - (?<value>[a-z-]+)$"));

        // The fuzz test projects have their own workflow, which uploads their results and reports them
        var testProjects = Directory.EnumerateDirectories(RepositoryRoot / "fuzz", "*.FuzzTests").Select(Path.GetFileName).Order(StringComparer.Ordinal).ToList();
        Assert.NotEmpty(testProjects);
        var testEntries = testProjects.Select(project => (Project: project, Workflows: workflows.Count(workflow => workflow.Text.Contains($"name: fuzz-results-{project}-", StringComparison.Ordinal) && workflow.Text.Contains($"-Project {project} ", StringComparison.Ordinal)))).ToList();
        Assert.Equal(testProjects.Select(project => (Project: project, Workflows: 1)).ToList(), testEntries);
    }

    [Fact]
    public void FuzzWorkflowsAreSpreadOverTheWeek()
    {
        // The SharpFuzz workflow has a schedule for each night of its projects, and the other fuzz workflows a weekly one.
        // The nights get the same number of fuzz campaigns, give or take one, so that a new project takes a night with the
        // fewest.
        var workflows = GetFuzzWorkflows();
        var sharpFuzz = workflows.Single(workflow => workflow.Name is SharpFuzzWorkflow).Text;
        var table = GetSharpFuzzProjects(sharpFuzz);
        Assert.Equal(table.Select(project => project.Day).Distinct().Order().ToList(), GetScheduledDays(sharpFuzz));

        var others = workflows.Where(workflow => workflow.Name is not SharpFuzzWorkflow).Select(workflow => (workflow.Name, Days: GetScheduledDays(workflow.Text))).ToList();
        Assert.Equal(others.Select(workflow => (workflow.Name, Schedules: 1)).ToList(), others.Select(workflow => (workflow.Name, Schedules: workflow.Days.Count)).ToList());

        var campaignsPerNight = Enumerable.Range(0, 7).Select(day => table.Count(project => project.Day == day) + others.Count(workflow => workflow.Days[0] == day)).ToList();
        Assert.True(campaignsPerNight.Max() - campaignsPerNight.Min() <= 1, $"Fuzz campaigns per night, from Sunday: {string.Join(", ", campaignsPerNight)}");
    }

    private const string SharpFuzzWorkflow = "fuzz-sharpfuzz.yml";

    private static List<(string Name, string Text)> GetFuzzWorkflows()
    {
        return [.. Directory.EnumerateFiles(RepositoryRoot / ".github" / "workflows", "fuzz-*.yml").Order(StringComparer.Ordinal).Select(path => (Path.GetFileName(path), File.ReadAllText(path).ReplaceLineEndings("\n")))];
    }

    // The FUZZ_PROJECTS JSON array of the SharpFuzz workflow
    private static List<(string Project, string Component, int Day)> GetSharpFuzzProjects(string workflow)
    {
        const string Start = "\n  FUZZ_PROJECTS: |\n";
        const string End = "\n    ]\n";
        var start = workflow.IndexOf(Start, StringComparison.Ordinal);
        Assert.True(start >= 0, "FUZZ_PROJECTS not found");
        var end = workflow.IndexOf(End, start, StringComparison.Ordinal);
        Assert.True(end >= 0, "End of FUZZ_PROJECTS not found");

        using var document = JsonDocument.Parse(workflow[(start + Start.Length)..(end + End.Length)]);
        return [.. document.RootElement.EnumerateArray().Select(project => (project.GetProperty("project").GetString()!, project.GetProperty("component").GetString()!, project.GetProperty("day").GetInt32()))];
    }

    // The days of the week of the schedules, at 3:00 in Toronto
    private static List<int> GetScheduledDays(string workflow)
    {
        return [.. Matches(workflow, @"^    - cron: '0 3 \* \* (?<value>[0-6])'[^\n]*\n      timezone: America/Toronto$").Select(day => int.Parse(day, CultureInfo.InvariantCulture))];
    }

    private static List<string> Matches(string text, [StringSyntax(StringSyntaxAttribute.Regex)] string pattern)
    {
        return [.. Regex.Matches(text, pattern, RegexOptions.Multiline, TimeSpan.FromSeconds(10)).Select(match => match.Groups["value"].Value)];
    }
}
