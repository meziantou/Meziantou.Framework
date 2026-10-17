using System.Diagnostics;
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
    public void EveryFuzzProjectIsTestedAndFuzzedWeekly()
    {
        var projects = Directory.EnumerateDirectories(RepositoryRoot / "fuzz", "*.Fuzz").Select(Path.GetFileName).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(projects, FuzzProjects.All.Select(project => project.Project).Order(StringComparer.Ordinal).ToList());

        // Each project is in the matrix of the fuzz job and in the matrix of the report job, which labels its issues
        var workflow = File.ReadAllText(RepositoryRoot / ".github" / "workflows" / "fuzz.yml");
        var entries = projects.Select(project => (Project: project, Entries: workflow.Split($"project: {project}, component: ").Length - 1)).ToList();
        Assert.Equal(projects.Select(project => (Project: project, Entries: 2)).ToList(), entries);
    }
}
