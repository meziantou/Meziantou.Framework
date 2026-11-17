using System.Buffers.Binary;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>Reads <c>multiProcessElementsType</c> tags and their processing elements.</summary>
internal static class IccMpeParser
{
    public const uint TypeMultiProcessElements = 0x6D706574; // 'mpet'
    public const uint ElementCurveSet = 0x63767374; // 'cvst'
    public const uint ElementMatrix = 0x6D617466; // 'matf'
    public const uint ElementClut = 0x636C7574; // 'clut'
    public const uint ElementCalculator = 0x63616C63; // 'calc'
    public const uint ElementTintArray = 0x74696E74; // 'tint'
    public const uint ElementBeginAcs = 0x62414353; // 'bACS'
    public const uint ElementEndAcs = 0x65414353; // 'eACS'
    private const uint ElementXyzToJab = 0x58746F4A; // 'XtoJ'
    private const uint ElementJabToXyz = 0x4A746F58; // 'JtoX'

    /// <summary>
    /// Reads a <c>multiProcessElementsType</c> tag (ICC.2:2023 section 10.2.17, ICC.1:2022 section 10.16) whose input and
    /// output channels are the expected ones, and creates the stage that evaluates it.
    /// </summary>
    /// <exception cref="InvalidImageContentException">The tag is malformed.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The tag uses a processing element that is not supported.</exception>
    public static IccMpeStage CreateStage(ReadOnlyMemory<byte> tag, int inputs, int outputs, IccMpeParseContext context)
    {
        var span = tag.Span;
        if (span.Length < 16 || IccReader.ReadUInt32(span) != TypeMultiProcessElements)
            throw context.Invalid("the tag is not a multiProcessElementsType.");

        var inputCount = IccReader.ReadUInt16(span[8..]);
        var outputCount = IccReader.ReadUInt16(span[10..]);
        if (inputCount != inputs || outputCount != outputs)
            throw context.Invalid(string.Create(CultureInfo.InvariantCulture, $"the multiProcessElementsType has {inputCount} inputs and {outputCount} outputs instead of {inputs} and {outputs}."));

        var count = IccReader.ReadUInt32(span[12..]);
        if (count == 0 || 16 + ((long)count * 8) > span.Length)
            throw context.Invalid(string.Create(CultureInfo.InvariantCulture, $"the multiProcessElementsType declares {count} processing elements, which do not fit in its {span.Length} bytes."));

        // Elements with the same position are the same element. Each value of the data is stored at most once, plus the
        // samples of the calculator curves; references to overlapping data would otherwise allocate it again and again
        context.AllocationLimit = span.Length + (1L << 20);
        var elements = new IccMpeElement[count];
        var cache = new Dictionary<(uint Offset, uint Size), IccMpeElement>();
        var channels = (int)inputCount;
        int widest = Math.Max(inputCount, outputCount);
        var scratch = 0;
        long cost = 0;
        for (var i = 0; i < elements.Length; i++)
        {
            var key = (IccReader.ReadUInt32(span[(16 + (8 * i))..]), IccReader.ReadUInt32(span[(20 + (8 * i))..]));
            if (!cache.TryGetValue(key, out var element))
            {
                element = ParseElement(GetPosition(tag, 16 + (8 * i), context), context);
                cache[key] = element;
            }

            if (element.InputCount != channels)
                throw context.Invalid(string.Create(CultureInfo.InvariantCulture, $"the processing element {i} has {element.InputCount} inputs, but the previous element gives {channels} values."));

            channels = element.OutputCount;
            widest = Math.Max(widest, channels);
            scratch = Math.Max(scratch, element.ScratchSize);
            cost += element.Cost;
            elements[i] = element;
        }

        if (channels != outputCount)
            throw context.Invalid(string.Create(CultureInfo.InvariantCulture, $"the last processing element gives {channels} values instead of {outputCount}."));

        if (cost > IccMpeParseContext.MaxCost)
            throw context.Unsupported(string.Create(CultureInfo.InvariantCulture, $"the processing elements need up to {cost} operations per color, more than the {IccMpeParseContext.MaxCost} supported."), "ICC processing elements too costly to evaluate");

        if ((2L * widest) + scratch > IccMpeParseContext.MaxChannels * 4L)
            throw context.Unsupported("the processing elements need more memory per color than supported.", "ICC processing elements too large to evaluate");

        return new IccMpeStage(elements, inputCount, outputCount, widest, scratch);
    }

    /// <summary>Reads a processing element (ICC.2:2023 clause 11).</summary>
    public static IccMpeElement ParseElement(ReadOnlyMemory<byte> element, IccMpeParseContext context)
    {
        var span = element.Span;
        if (span.Length < 12)
            throw context.Invalid("a processing element is shorter than its header.");

        var signature = IccReader.ReadUInt32(span);
        int inputs = IccReader.ReadUInt16(span[8..]);
        int outputs = IccReader.ReadUInt16(span[10..]);
        if (inputs == 0 || outputs == 0)
            throw context.Invalid($"the processing element {IccReader.FormatSignature(signature)} has no input or no output channel.");

        // Each element and its arrays per channel (the curves of a curve set, the grid of a color lookup table) are charged
        // before they are parsed: elements at overlapping positions of different sizes are distinct elements, which would
        // otherwise store them again and again while charging only their data, possibly none
        context.Allocate(1 + Math.Max(inputs, outputs));

        return signature switch
        {
            ElementCurveSet => IccMpeCurveSet.Parse(element, inputs, outputs, context),
            ElementMatrix => IccMpeMatrix.Parse(span, inputs, outputs, context),
            ElementClut => IccMpeClut.Parse(element, inputs, outputs, context),
            ElementCalculator => IccMpeCalculator.Parse(element, inputs, outputs, context),
            ElementTintArray => IccMpeTintArray.Parse(span, inputs, outputs, context),
            ElementBeginAcs or ElementEndAcs => inputs == outputs && span.Length >= 16 ? new IccMpePassThrough(inputs) : throw context.Invalid($"the {IccReader.FormatSignature(signature)} element must have as many outputs as inputs and a signature."),
            ElementXyzToJab or ElementJabToXyz => throw context.Unsupported($"the color appearance element {IccReader.FormatSignature(signature)} is not supported.", "ICC color appearance processing element"),
            _ => throw context.Unsupported($"the processing element {IccReader.FormatSignature(signature)} is not supported; only curve sets, matrices, color lookup tables, calculators and tint arrays are.", "ICC processing element"),
        };
    }

    /// <summary>A positionNumber (an offset and a size relative to the start of the containing structure) inside the structure.</summary>
    public static ReadOnlyMemory<byte> GetPosition(ReadOnlyMemory<byte> container, int position, IccMpeParseContext context)
    {
        var span = container.Span;
        var offset = IccReader.ReadUInt32(span[position..]);
        var size = IccReader.ReadUInt32(span[(position + 4)..]);
        if (offset > (uint)span.Length || size > (uint)span.Length - offset)
            throw context.Invalid(string.Create(CultureInfo.InvariantCulture, $"a processing element ({size} bytes at offset {offset}) is outside its container."));

        return container.Slice((int)offset, (int)size);
    }

    /// <summary>Reads a float32Number and rejects the values that are not finite numbers.</summary>
    public static double ReadFiniteSingle(ReadOnlySpan<byte> data, IccMpeParseContext context, string what)
    {
        var value = BinaryPrimitives.ReadSingleBigEndian(data);
        if (!float.IsFinite(value))
            throw context.Invalid($"a {what} is not a finite number.");

        return value;
    }

    /// <summary>The size of a value of a valueEncodingType (ICC.2:2023 section 4.2.10), or of the numeric array type whose signature some encoders write instead.</summary>
    public static int GetValueSize(uint encoding) => encoding switch
    {
        0 or 0x666C3332 => 4, // float32Number, 'fl32'
        1 or 0x666C3136 => 2, // float16Number, 'fl16'
        2 or 0x75693136 => 2, // uInt16Number, 'ui16'
        3 or 0x75693038 => 1, // uInt8Number, 'ui08'
        _ => 0,
    };

    /// <summary>Reads a value of a valueEncodingType: the integers encode [0, 1].</summary>
    public static double ReadValue(ReadOnlySpan<byte> data, uint encoding) => encoding switch
    {
        0 or 0x666C3332 => BinaryPrimitives.ReadSingleBigEndian(data),
        1 or 0x666C3136 => (double)BinaryPrimitives.ReadHalfBigEndian(data),
        2 or 0x75693136 => BinaryPrimitives.ReadUInt16BigEndian(data) / 65535.0,
        _ => data[0] / 255.0,
    };
}
