using System.Xml.Linq;
using Meziantou.Framework.Imaging.TestHarness.Golden;

namespace Meziantou.Framework.Imaging.Tests.Conformance;

/// <summary>
/// The generic suites over the corpus run once per codec test project, each on the fixture formats and the encoders its
/// project file declares (<see cref="CorpusScope"/>). Every format of the corpus and every encoder must belong to exactly one
/// project: a format nobody declares would not be tested at all, and one declared twice would be tested twice.
/// </summary>
public sealed class CorpusScopeTests
{
    private static readonly FullPath ProjectDirectory = FullPath.FromPath(AppContext.BaseDirectory) / "TestProjects";

    [Fact]
    public void EveryFixtureFormatBelongsToOneTestProject()
    {
        var formats = GoldenCorpus.Default.Fixtures.Select(fixture => fixture.Entry.Format).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        AssertDeclaredOnce(CorpusScope.FixtureFormatsPropertyName, formats);
    }

    [Fact]
    public void EveryEncoderBelongsToOneTestProject()
        => AssertDeclaredOnce(CorpusScope.EncodersPropertyName, [.. CorpusScope.AllEncoderNames.Order(StringComparer.Ordinal)]);

    [Fact]
    public void ThisProjectRunsNoGenericSuite()
    {
        // Its tests are not about one codec; the suites it would compile are the ones of CorpusScope
        Assert.Empty(CorpusScope.EncoderNames);
        Assert.Empty(CorpusScope.Fixtures);
    }

    private static void AssertDeclaredOnce(string propertyName, string[] expected)
    {
        var declarations = new List<(string Value, string Project)>();
        foreach (var path in Directory.EnumerateFiles(ProjectDirectory, "*.xml"))
        {
            var project = XDocument.Load(path);
            foreach (var property in project.Descendants(propertyName))
            {
                declarations.AddRange(CorpusScope.ParseList(property.Value).Select(value => (value, Path.GetFileNameWithoutExtension(path))));
            }
        }

        var duplicates = declarations.GroupBy(declaration => declaration.Value, StringComparer.Ordinal).Where(group => group.Count() > 1)
            .Select(group => $"{group.Key}: {string.Join(", ", group.Select(declaration => declaration.Project))}");
        Assert.Empty(duplicates);
        Assert.Equal(expected, declarations.Select(declaration => declaration.Value).Order(StringComparer.Ordinal));
    }
}
