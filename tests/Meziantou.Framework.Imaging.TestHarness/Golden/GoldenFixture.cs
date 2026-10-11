using System.Security.Cryptography;
using Meziantou.Framework.Imaging.TestHarness.Fixtures;
using Meziantou.Framework.Imaging.TestHarness.Pixels;

namespace Meziantou.Framework.Imaging.TestHarness.Golden;

/// <summary>A fixture of a validated corpus with typed access to its input, expectations and reference buffers.</summary>
public sealed class GoldenFixture
{
    internal GoldenFixture(FullPath rootDirectory, FixtureEntry entry)
    {
        RootDirectory = rootDirectory;
        Entry = entry;
        InputPath = FixturePaths.TryResolve(rootDirectory, entry.Input.Path, out _) ?? throw new InvalidOperationException($"Invalid input path for fixture '{entry.Id}'.");
    }

    /// <summary>Gets the manifest entry.</summary>
    public FixtureEntry Entry { get; }

    /// <summary>Gets the fixture id.</summary>
    public string Id => Entry.Id;

    /// <summary>Gets the corpus root.</summary>
    public FullPath RootDirectory { get; }

    /// <summary>Gets the absolute path of the encoded input.</summary>
    public FullPath InputPath { get; }

    /// <summary>Gets a value indicating whether the fixture is valid (has expected pixels).</summary>
    public bool IsValid => Entry.Kind == FixtureKinds.Valid;

    /// <summary>Gets the expected image (valid fixtures only).</summary>
    public FixtureExpectation Expected => Entry.Expected ?? throw new InvalidOperationException($"Fixture '{Id}' is a {Entry.Kind} fixture and has no expected pixels; use ExpectedError.");

    /// <summary>Gets the expected error (invalid, unsupported and limit fixtures only).</summary>
    public ExpectedErrorDefinition ExpectedError => Entry.ExpectedError ?? throw new InvalidOperationException($"Fixture '{Id}' is valid and has no expected error.");

    /// <summary>Gets the comparison policy (valid fixtures only).</summary>
    public ComparisonPolicy Policy => ComparisonPolicy.FromDefinition(Entry.Comparison ?? throw new InvalidOperationException($"Fixture '{Id}' has no comparison policy."));

    /// <summary>Gets the reference layouts available for every frame (and the poster).</summary>
    public IReadOnlyList<RawPixelLayout> Layouts => [.. Expected.Frames[0].Buffers.Select(buffer => RawPixelLayout.Parse(buffer.Layout))];

    /// <summary>Gets the canonical layout (<c>rgba8</c> or <c>rgba16le</c>) of the references.</summary>
    public RawPixelLayout CanonicalLayout => Layouts.Contains(RawPixelLayout.Rgba16Le) ? RawPixelLayout.Rgba16Le : RawPixelLayout.Rgba8;

    /// <summary>Reads the encoded input, verifying its hash.</summary>
    /// <returns>The bytes.</returns>
    public byte[] ReadInput()
    {
        var data = File.ReadAllBytes(InputPath);
        var hash = Convert.ToHexStringLower(SHA256.HashData(data));
        if (!string.Equals(hash, Entry.Input.Sha256, StringComparison.Ordinal))
            throw new InvalidDataException($"Fixture '{Id}': input sha256 {hash} does not match the manifest ({Entry.Input.Sha256}).");

        return data;
    }

    /// <summary>Reads the raw timing, loop and metadata fields of the encoded input with the independent <see cref="EncodedFieldInspector"/>.</summary>
    /// <returns>The raw fields.</returns>
    public EncodedFields InspectInput() => EncodedFieldInspector.Inspect(Entry.Format, ReadInput());

    /// <summary>Opens the encoded input as a read-only stream.</summary>
    /// <returns>The stream.</returns>
    public Stream OpenInput() => new MemoryStream(ReadInput(), writable: false);

    /// <summary>Gets the expected exact duration of a displayed frame.</summary>
    /// <param name="index">The frame index.</param>
    /// <returns>The duration.</returns>
    public RationalDuration GetDuration(int index) => RationalDuration.Parse(GetFrameExpectation(index).Duration!);

    /// <summary>Reads the reference of a displayed frame in a layout.</summary>
    /// <param name="index">The frame index.</param>
    /// <param name="layout">The layout; must be one of <see cref="Layouts"/>.</param>
    /// <returns>The buffer.</returns>
    public RawPixelBuffer GetFrame(int index, RawPixelLayout layout) => ReadBuffer(GetFrameExpectation(index), layout, $"frame {index.ToString(CultureInfo.InvariantCulture)}");

    /// <summary>Reads the reference of the separate poster in a layout.</summary>
    /// <param name="layout">The layout; must be one of <see cref="Layouts"/>.</param>
    /// <returns>The buffer.</returns>
    public RawPixelBuffer GetPoster(RawPixelLayout layout)
        => ReadBuffer(Expected.Poster ?? throw new InvalidOperationException($"Fixture '{Id}' has no separate poster."), layout, "poster");

    /// <inheritdoc/>
    public override string ToString() => Id;

    private FrameExpectation GetFrameExpectation(int index)
    {
        var frames = Expected.Frames;
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        if (index >= frames.Count)
            throw new ArgumentOutOfRangeException(nameof(index), index, $"Fixture '{Id}' has {frames.Count.ToString(CultureInfo.InvariantCulture)} frames.");

        return frames[index];
    }

    private RawPixelBuffer ReadBuffer(FrameExpectation frame, RawPixelLayout layout, string role)
    {
        ArgumentNullException.ThrowIfNull(layout);
        var descriptor = frame.Buffers.FirstOrDefault(buffer => buffer.Layout == layout.Name)
            ?? throw new ArgumentException($"Fixture '{Id}' {role} has no {layout} reference (available: {string.Join(", ", frame.Buffers.Select(buffer => buffer.Layout))}). Add the layout to the generator rather than converting references in tests.", nameof(layout));
        return RawBufferReader.Read(RootDirectory, descriptor);
    }
}
