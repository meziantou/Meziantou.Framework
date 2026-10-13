using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.TestHarness.Golden;

namespace Meziantou.Framework.Imaging.Tests.Conformance;

/// <summary>
/// The coding tools the HEVC conformance vectors exercise, as the decoder reports them for what it decoded
/// (<see cref="HevcDecoder.Tools"/>): the feature tags of a vector only describe its headers, which do not tell whether a
/// block uses an intra mode, a merge candidate or a filter. Together, the vectors must reach every tool of
/// <see cref="HevcTools"/>, except the ones that no encoder of the corpus generator writes, which the streams of the unit
/// tests reach (<see cref="NotReachedByTheVectors"/>).
/// </summary>
public sealed class HevcToolCoverageTests
{
    /// <summary>
    /// The tools that no vector reaches: no encoder of the pinned tools writes long-term reference pictures or a modified
    /// reference picture list. <c>HevcInterDecoderTests</c> decodes streams whose headers were rewritten to use them, which
    /// the reference decoders confirmed.
    /// </summary>
    private const HevcTools NotReachedByTheVectors = HevcTools.LongTermReferences | HevcTools.ReferenceListModification;

    private static readonly Lazy<Dictionary<string, HevcTools>> ToolsByVector = new(() => GoldenCorpus.Default.HevcVectors
        .Where(vector => !vector.HasFeatureKey("hevc.unsupported"))
        .ToDictionary(vector => vector.Id, vector => Decode(vector.Id), StringComparer.Ordinal));

    [Fact]
    public void TheVectorsReachEveryTool()
    {
        var reached = ToolsByVector.Value.Values.Aggregate(HevcTools.None, (tools, vector) => tools | vector);
        var missing = Enum.GetValues<HevcTools>().Where(tool => tool is not HevcTools.None && !reached.HasFlag(tool) && !NotReachedByTheVectors.HasFlag(tool)).ToList();
        Assert.True(missing.Count is 0, $"No HEVC vector reaches {string.Join(", ", missing)}: add a vector to the corpus generator (HevcCorpus.*.cs).");

        // An exclusion that a vector reaches is not one any more
        Assert.Equal(HevcTools.None, reached & NotReachedByTheVectors);
    }

    [Fact]
    public void EveryToolIsASingleFlag()
    {
        foreach (var tool in Enum.GetValues<HevcTools>())
        {
            if (tool is not HevcTools.None)
                Assert.True(ulong.IsPow2((ulong)tool), $"{tool} is not a single flag.");
        }
    }

    private static HevcTools Decode(string id)
    {
        var corpus = GoldenCorpus.Default;
        HevcDecoderConformanceTests.Decode(corpus, corpus.GetHevcVector(id), out var tools);
        return tools;
    }
}
