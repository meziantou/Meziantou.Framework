using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.TestHarness.Golden;

namespace Meziantou.Framework.Imaging.Tests.Conformance;

/// <summary>
/// The coding tools the AV1 conformance vectors exercise, as the decoder reports them for the blocks it decoded
/// (<see cref="Av1Decoder.Tools"/>): the feature tags of a vector only describe its headers, which do not tell whether a
/// block uses a compound type, a motion mode or intra block copy. Together, the vectors must reach every tool of
/// <see cref="Av1Tools"/>: there is no exclusion.
/// </summary>
/// <remarks>
/// Four tools are coded by neither encoder of the corpus generator (libaom 3.15.1 and SVT-AV1 4.2.0, whatever their
/// options). They are reached by vectors that an encoder did not write alone, and each of them is required on those
/// vectors by name, so that the coverage cannot move silently to another stream:
/// <list type="bullet">
/// <item><description>
/// the bilinear interpolation filter of a whole frame: the <c>av1.patched_header</c> vectors, whose 2-bit
/// <c>interpolation_filter</c> element is overwritten in frame headers that code <c>is_filter_switchable = 0</c> (no other
/// syntax element changes), and the last frame of the <c>dual-filters</c> hand-authored vectors;
/// </description></item>
/// <item><description>
/// an interpolation filter per direction (<c>enable_dual_filter</c>), a predicted segment id (<c>seg_id_predicted = 1</c>)
/// and the reference frame feature of a segment, which change the syntax of the blocks: the <c>av1.hand_authored</c>
/// vectors, a libaom key frame followed by inter frames written bit by bit from the specification by the generator
/// (<c>Av1HandAuthoredStream</c>).
/// </description></item>
/// </list>
/// </remarks>
public sealed class Av1ToolCoverageTests
{
    private static readonly Lazy<Dictionary<string, Av1Tools>> ToolsByVector = new(() => GoldenCorpus.Default.Av1Vectors.ToDictionary(vector => vector.Id, vector => Decode(vector.Id), StringComparer.Ordinal));

    [Fact]
    public void TheVectorsReachEveryTool()
    {
        var reached = ToolsByVector.Value.Values.Aggregate(Av1Tools.None, (tools, vector) => tools | vector);
        var missing = Enum.GetValues<Av1Tools>().Where(tool => tool is not Av1Tools.None && !reached.HasFlag(tool)).ToList();
        Assert.Empty(missing, $"No AV1 vector reaches {string.Join(", ", missing)}: add a vector to the corpus generator (AvifCorpus.Vectors.cs). The vectors reach {reached}.");
    }

    [Fact]
    public void PatchedVectorsReachTheBilinearFilter()
    {
        var patched = GoldenCorpus.Default.Av1Vectors.Where(vector => vector.HasFeature("av1.patched_header") && vector.HasFeature("av1.interpolation_filter=bilinear")).ToList();
        Assert.NotEmpty(patched);
        Assert.All(patched, vector => Assert.True(ToolsByVector.Value[vector.Id].HasFlag(Av1Tools.BilinearInterpolationFilter), $"{vector.Id} does not reach the bilinear filter."));
    }

    [Fact]
    public void BilinearFilterIsOnlyReachedByTheVectorsThatSignalIt()
    {
        // Neither encoder selects the bilinear filter, for a frame or for a block: a vector reaches it when a frame header
        // signals it, which only the patched and the hand-authored vectors do
        foreach (var vector in GoldenCorpus.Default.Av1Vectors)
        {
            var signalled = vector.HasFeature("av1.interpolation_filter=bilinear");
            Assert.True(!signalled || vector.HasFeature("av1.patched_header") || vector.HasFeature("av1.hand_authored"), $"{vector.Id} signals the bilinear filter although an encoder wrote it.");
            Assert.Equal(signalled, ToolsByVector.Value[vector.Id].HasFlag(Av1Tools.BilinearInterpolationFilter), message: $"{vector.Id}: the bilinear filter is reached exactly when a frame header signals it.");
        }
    }

    [Theory]
    [InlineData("hand-dual-filters", nameof(Av1Tools.DualInterpolationFilter) + ", " + nameof(Av1Tools.BilinearInterpolationFilter) + ", " + nameof(Av1Tools.NewMotionVector))]
    [InlineData("hand-segment-features", nameof(Av1Tools.PredictedSegmentId) + ", " + nameof(Av1Tools.SegmentReferenceFrame) + ", " + nameof(Av1Tools.IntraBlockInInterFrame) + ", " + nameof(Av1Tools.GlobalMotionVector))]
    public void HandAuthoredVectorsReachTheToolsNoEncoderCodes(string recipe, string toolNames)
    {
        // Av1Tools is internal: the theory data name its members
        var tools = Enum.Parse<Av1Tools>(toolNames);
        var vectors = GoldenCorpus.Default.Av1Vectors.Where(vector => vector.Id.StartsWith("av1/" + recipe + "-", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(vectors);
        Assert.All(vectors, vector =>
        {
            TestContext.Current.TestOutputHelper?.WriteLine($"{vector.Id}: {ToolsByVector.Value[vector.Id]}");
            Assert.True(vector.HasFeature("av1.hand_authored"), $"{vector.Id} is not tagged as hand-authored.");
            Assert.True(ToolsByVector.Value[vector.Id].HasFlag(tools), $"{vector.Id} reaches {ToolsByVector.Value[vector.Id]}, which does not include {tools}.");
        });

        // Every subsampling: the filters and the segment features apply to the chroma planes with their own block sizes
        Assert.Equal(["4:0:0", "4:2:0", "4:2:2", "4:4:4"], vectors.Select(vector => vector.Subsampling).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal([8, 10, 12], vectors.Select(vector => vector.BitDepth).Distinct().Order().ToArray());
    }

    [Theory]
    [InlineData(nameof(Av1Tools.DualInterpolationFilter))]
    [InlineData(nameof(Av1Tools.PredictedSegmentId))]
    [InlineData(nameof(Av1Tools.SegmentReferenceFrame))]
    public void ToolsCodedBlockByBlockAreOnlyReachedByHandAuthoredVectors(string toolName)
    {
        var tool = Enum.Parse<Av1Tools>(toolName);
        // If an encoder starts to code one of them, the vector that shows it is worth a name and a required tag
        var reaching = GoldenCorpus.Default.Av1Vectors.Where(vector => ToolsByVector.Value[vector.Id].HasFlag(tool)).ToList();
        Assert.NotEmpty(reaching);
        Assert.All(reaching, vector => Assert.True(vector.HasFeature("av1.hand_authored"), $"{vector.Id} reaches {tool} without being a hand-authored vector."));
    }

    [Fact]
    public void EveryToolIsASingleFlag()
    {
        // A combined member would be "reached" by one of its parts only by accident of HasFlag
        Assert.All(Enum.GetValues<Av1Tools>().Where(tool => tool is not Av1Tools.None), tool => Assert.True(ulong.IsPow2((ulong)tool), $"{tool} is not a single flag."));
    }

    private static Av1Tools Decode(string id)
    {
        var vector = GoldenCorpus.Default.GetAv1Vector(id);
        var data = File.ReadAllBytes(GoldenCorpus.Default.RootDirectory / vector.Input.Path);
        var scope = AllocationScope.Create(ImageConfiguration.Default, vector.Id);
        using var decoder = new Av1Decoder(scope);
        decoder.SetInput(data, 0, data.Length);
        while (decoder.DecodeNextOutputFrame(CancellationToken.None) is { } frame)
        {
            frame.Release();
        }

        return decoder.Tools;
    }
}
