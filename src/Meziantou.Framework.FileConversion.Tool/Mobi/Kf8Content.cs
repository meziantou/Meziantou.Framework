using System.Buffers.Binary;
using System.Text.RegularExpressions;
using Meziantou.Framework.FileConversionTool.Epub;
using Meziantou.Framework.Imaging;

namespace Meziantou.Framework.FileConversionTool.Mobi;

/// <summary>
/// The content of the KF8 version of a book. Its text is split into flows (FDST): the first flow holds the XHTML documents
/// (parts), cut into a skeleton and the fragments inserted into it, and the other flows hold style sheets and SVG images.
/// The <c>kindle:embed</c>, <c>kindle:flow</c> and <c>kindle:pos</c> URLs are replaced with the URLs that
/// <see cref="MobiDocumentLoader"/> serves.
/// </summary>
internal sealed partial class Kf8Content
{
    private const string PartsPath = "parts/";
    private const string FlowsPath = "flows/";

    // The tags of the entries of the skeleton and fragment indexes
    private const int SkeletonFragmentCountTag = 1;
    private const int FileNumberTag = 3;
    private const int PositionTag = 6;

    private Kf8Content(IReadOnlyList<byte[]> parts, IReadOnlyList<bool> partsAreXml, IReadOnlyList<byte[]> flows, IReadOnlySet<int> resourceIndexes, IReadOnlyList<int>? imagePages)
    {
        Parts = parts;
        PartsAreXml = partsAreXml;
        Flows = flows;
        ResourceIndexes = resourceIndexes;
        ImagePages = imagePages;
    }

    /// <summary>Gets the XHTML documents of the book, in reading order, in UTF-8.</summary>
    public IReadOnlyList<byte[]> Parts { get; }

    /// <summary>Gets, for each part, whether it is well-formed XML, served as XHTML. The other parts are served as HTML.</summary>
    public IReadOnlyList<bool> PartsAreXml { get; }

    /// <summary>Gets the flows of the text. The first one is empty: its content is the parts.</summary>
    public IReadOnlyList<byte[]> Flows { get; }

    /// <summary>Gets the numbers of the resources the parts and the flows use.</summary>
    public IReadOnlySet<int> ResourceIndexes { get; }

    /// <summary>Gets the resource of the image of each part, when each part shows one image of a supported format (comics), or null.</summary>
    public IReadOnlyList<int>? ImagePages { get; }

    public static string GetPartUrl(int index) => MobiHtml.BaseUrl + PartsPath + index.ToString(CultureInfo.InvariantCulture) + ".xhtml";

    /// <summary>Gets the URL of a flow. Its extension gives its media type.</summary>
    public static string GetFlowUrl(int index, string? mediaType)
    {
        var extension = mediaType?.ToLowerInvariant() switch
        {
            "text/css" => ".css",
            "image/svg+xml" => ".svg",
            _ => "",
        };

        return MobiHtml.BaseUrl + FlowsPath + index.ToString(CultureInfo.InvariantCulture) + extension;
    }

    /// <summary>Gets the index of the part of a URL, or null when the URL is not the one of a part.</summary>
    public static int? GetPartIndex(string url) => ParseIndex(url, PartsPath, out _);

    /// <summary>Gets the index of the flow of a URL, and its extension, or null when the URL is not the one of a flow.</summary>
    public static int? GetFlowIndex(string url, out string extension) => ParseIndex(url, FlowsPath, out extension);

    /// <summary>Reads the content of the KF8 version of a book.</summary>
    /// <exception cref="InvalidDataException">The text, its flows or its indexes are not valid.</exception>
    public static Kf8Content Read(MobiBook book, MobiHeader header)
    {
        var text = book.ReadText(header);
        var flowRanges = ReadFlowRanges(book, header, text.Length);
        var (first, firstEnd) = flowRanges[0];
        var (parts, fragments) = BuildParts(book, header, text.AsSpan(first, firstEnd - first));

        var resources = new HashSet<int>();
        var flows = new List<byte[]> { Array.Empty<byte>() };
        for (var i = 1; i < flowRanges.Count; i++)
        {
            var (start, end) = flowRanges[i];
            flows.Add(ConvertUrls(text.AsSpan(start, end - start).ToArray(), resources, links: null, flows: null, currentPart: -1));
        }

        var links = new LinkResolver(parts, fragments);
        var convertedParts = new List<byte[]>(parts.Count);
        for (var i = 0; i < parts.Count; i++)
        {
            convertedParts.Add(ConvertUrls(parts[i].Data, resources, links, flows, i));
        }

        var partsAreXml = new List<bool>(convertedParts.Count);
        List<int>? images = new(convertedParts.Count);
        foreach (var part in convertedParts)
        {
            System.Xml.XmlDocument? document;
            try
            {
                document = EpubPackage.ParseXml(part, "part");
            }
            catch (InvalidDataException)
            {
                document = null;
            }

            partsAreXml.Add(document is not null);
            if (images is not null && document is not null && EpubImagePages.FindSingleImage(document) is { } source && MobiHtml.GetImageIndex(source) is { } index && IsImage(book, index))
            {
                images.Add(index);
            }
            else
            {
                images = null;
            }
        }

        return new Kf8Content(convertedParts, partsAreXml, flows, resources, images is { Count: > 0 } ? images : null);
    }

    private static bool IsImage(MobiBook book, int index)
        => book.GetImage(index) is { } data && Image.DetectFormat(data.Span) is not (ImageFormat.Unknown or ImageFormat.Custom);

    private static int? ParseIndex(string url, string path, out string extension)
    {
        extension = "";
        var prefix = MobiHtml.BaseUrl + path;
        if (!url.StartsWith(prefix, StringComparison.Ordinal))
            return null;

        var value = MobiHtml.RemoveQueryAndFragment(url.AsSpan(prefix.Length));
        var dot = value.IndexOf('.');
        if (dot >= 0)
        {
            extension = value[dot..].ToString();
            value = value[..dot];
        }

        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var index) ? index : null;
    }

    private static List<(int Start, int End)> ReadFlowRanges(MobiBook book, MobiHeader header, int textLength)
    {
        if (header.FdstIndex < 0)
            return [(0, textLength)];

        var fdst = book.GetRecord(header.FdstIndex).Span;
        if (fdst.Length < 12 || !fdst.StartsWith("FDST"u8))
            throw new InvalidDataException("The FDST record of the book is not valid");

        var count = BinaryPrimitives.ReadUInt32BigEndian(fdst[8..]);
        if (count is 0 || count > (fdst.Length - 12) / 8)
            throw new InvalidDataException("The FDST record of the book is truncated");

        // Each flow ends where the next one starts, and the last one at the end of the text
        var starts = new List<int>((int)count);
        for (var i = 0; i < count; i++)
        {
            var start = BinaryPrimitives.ReadUInt32BigEndian(fdst[(12 + (i * 8))..]);
            if (start > textLength || (starts.Count > 0 && start < starts[^1]))
                throw new InvalidDataException("The flows of the book are not in order");

            starts.Add((int)start);
        }

        var result = new List<(int Start, int End)>(starts.Count);
        for (var i = 0; i < starts.Count; i++)
        {
            result.Add((starts[i], i + 1 < starts.Count ? starts[i + 1] : textLength));
        }

        return result;
    }

    private static (List<Part> Parts, List<Fragment> Fragments) BuildParts(MobiBook book, MobiHeader header, ReadOnlySpan<byte> text)
    {
        // A book without skeletons is one document
        if (header.SkeletonIndex < 0)
            return ([new Part(text.ToArray(), 0, text.Length)], []);

        var fragments = new List<Fragment>();
        if (header.FragmentIndex >= 0)
        {
            foreach (var entry in MobiIndex.Read(book, header.FragmentIndex))
            {
                // The name of an entry is the position the fragment is inserted at, in decimal
                if (!long.TryParse(entry.Name, NumberStyles.None, CultureInfo.InvariantCulture, out var insertPosition))
                    throw new InvalidDataException("A fragment of the book has no valid position");

                var length = entry.GetValue(PositionTag, 1) ?? throw new InvalidDataException("A fragment of the book has no length");
                fragments.Add(new Fragment(insertPosition, length, entry.GetValue(FileNumberTag, 0)));
            }
        }

        var parts = new List<Part>();
        var fragmentIndex = 0;
        foreach (var entry in MobiIndex.Read(book, header.SkeletonIndex))
        {
            var fragmentCount = entry.GetValue(SkeletonFragmentCountTag, 0) ?? 0;
            var start = entry.GetValue(PositionTag, 0) ?? throw new InvalidDataException("A skeleton of the book has no position");
            var length = entry.GetValue(PositionTag, 1) ?? throw new InvalidDataException("A skeleton of the book has no length");
            if (start > text.Length || length > text.Length - start)
                throw new InvalidDataException("A skeleton of the book is outside of its text");

            if (fragmentCount > fragments.Count - fragmentIndex)
                throw new InvalidDataException("A skeleton of the book has more fragments than the fragment index");

            // The fragments follow their skeleton in the text, and are inserted into it at their positions
            var part = new List<byte>(text.Slice((int)start, (int)length).ToArray());
            var position = (long)start + length;
            for (var i = 0; i < fragmentCount; i++)
            {
                var fragment = fragments[fragmentIndex++];
                var insertPosition = fragment.InsertPosition - start;
                if (fragment.Length > text.Length - position || insertPosition < 0 || insertPosition > part.Count)
                    throw new InvalidDataException("A fragment of the book is outside of its skeleton or its text");

                part.InsertRange((int)insertPosition, text.Slice((int)position, (int)fragment.Length));
                position += fragment.Length;
            }

            parts.Add(new Part([.. part], (int)start, (int)position));
        }

        if (parts.Count is 0)
            throw new InvalidDataException("The book has no skeleton");

        return (parts, fragments);
    }

    private static byte[] ConvertUrls(byte[] data, HashSet<int> resources, LinkResolver? links, List<byte[]>? flows, int currentPart)
    {
        // Latin-1 maps each byte to one character: the bytes of the UTF-8 text are kept as they are
        var text = Encoding.Latin1.GetString(data);

        // An image that is an SVG flow is replaced with the SVG markup, as the Kindle readers do: an SVG image does not load
        // the images it shows, such as the image of a cover
        if (flows is not null)
        {
            text = SvgImageRegex().Replace(text, match =>
            {
                if (!TryParseBase32(match.Groups["index"].ValueSpan, out var index) || index is 0 || index >= flows.Count)
                    return match.Value;

                var flow = Encoding.Latin1.GetString(flows[index]);
                var start = flow.IndexOf("<svg", StringComparison.OrdinalIgnoreCase);
                return start >= 0 ? flow[start..] : match.Value;
            });
        }

        text = EmbedRegex().Replace(text, match =>
        {
            if (!TryParseBase32(match.Groups["index"].ValueSpan, out var index) || index is 0)
                return match.Value;

            resources.Add(index);
            return MobiHtml.GetImageUrl(index);
        });

        text = FlowRegex().Replace(text, match => TryParseBase32(match.Groups["index"].ValueSpan, out var index) ? GetFlowUrl(index, match.Groups["mime"].Value) : match.Value);
        if (links is not null)
        {
            text = PositionRegex().Replace(text, match =>
            {
                if (!TryParseBase32(match.Groups["fid"].ValueSpan, out var fid) || !TryParseBase32(match.Groups["offset"].ValueSpan, out var offset))
                    return match.Value;

                return links.Resolve(fid, offset, currentPart) ?? match.Value;
            });
        }

        return Encoding.Latin1.GetBytes(text);
    }

    /// <summary>Parses a number written in base 32 with the digits 0-9 and A-V.</summary>
    internal static bool TryParseBase32(ReadOnlySpan<char> value, out int result)
    {
        long number = 0;
        result = 0;
        if (value.IsEmpty)
            return false;

        foreach (var c in value)
        {
            var digit = c switch
            {
                >= '0' and <= '9' => c - '0',
                >= 'A' and <= 'V' => c - 'A' + 10,
                >= 'a' and <= 'v' => c - 'a' + 10,
                _ => -1,
            };

            if (digit < 0)
                return false;

            number = (number * 32) + digit;
            if (number > int.MaxValue)
                return false;
        }

        result = (int)number;
        return true;
    }

    [GeneratedRegex("""kindle:embed:(?<index>[0-9A-Va-v]{1,10})(?:\?mime=[^"'()\s<>]*)?""", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture | RegexOptions.NonBacktracking)]
    private static partial Regex EmbedRegex();

    [GeneratedRegex("""kindle:flow:(?<index>[0-9A-Va-v]{1,10})(?:\?mime=(?<mime>[^"'()\s<>]*))?""", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture | RegexOptions.NonBacktracking)]
    private static partial Regex FlowRegex();

    [GeneratedRegex("""<img\b[^<>]*kindle:flow:(?<index>[0-9A-Va-v]{1,10})\?mime=image/svg\+xml[^<>]*>""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture | RegexOptions.NonBacktracking)]
    private static partial Regex SvgImageRegex();

    [GeneratedRegex("""kindle:pos:fid:(?<fid>[0-9A-Va-v]{1,10}):off:(?<offset>[0-9A-Va-v]{1,10})""", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture | RegexOptions.NonBacktracking)]
    private static partial Regex PositionRegex();

    [GeneratedRegex("""<[A-Za-z][^<>]*\s(?:id|name)\s*=\s*["'](?<id>[^"'<>]*)["']""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture | RegexOptions.NonBacktracking)]
    private static partial Regex IdRegex();

    [GeneratedRegex("""<body\b""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex BodyRegex();

    /// <summary>A document of the book, and the positions of the text it comes from.</summary>
    private sealed record Part(byte[] Data, int Start, int End);

    /// <summary>A fragment of a document: the position it is inserted at, its length, and the number of its document.</summary>
    private sealed record Fragment(long InsertPosition, uint Length, uint? FileNumber);

    /// <summary>
    /// Resolves the <c>kindle:pos:fid:XXXX:off:YYYYYYYYYY</c> links (a position after the start of a fragment) to the
    /// document that holds the position, and the closest element with an <c>id</c> before it.
    /// </summary>
    private sealed class LinkResolver(List<Part> parts, List<Fragment> fragments)
    {
        private readonly Dictionary<int, (List<(int Position, string Id)> Ids, int BodyStart)> _anchors = [];

        public string? Resolve(int fid, int offset, int currentPart)
        {
            if (fid >= fragments.Count)
                return null;

            var fragment = fragments[fid];
            var position = fragment.InsertPosition + offset;
            var partIndex = FindPart(position);
            if (partIndex < 0)
            {
                // A position that no document holds goes to the start of the document of the fragment
                if (fragment.FileNumber is not { } fileNumber || fileNumber >= parts.Count)
                    return null;

                partIndex = (int)fileNumber;
                position = parts[partIndex].Start;
            }

            var id = FindId(partIndex, (int)(position - parts[partIndex].Start));
            if (id is not null && partIndex == currentPart)
                return "#" + id;

            return GetPartUrl(partIndex) + (id is null ? "" : "#" + id);
        }

        private int FindPart(long position)
        {
            for (var i = 0; i < parts.Count; i++)
            {
                if (position >= parts[i].Start && position < parts[i].End)
                    return i;
            }

            return -1;
        }

        private string? FindId(int partIndex, int position)
        {
            var data = parts[partIndex].Data;
            if (!_anchors.TryGetValue(partIndex, out var anchors))
            {
                var text = Encoding.Latin1.GetString(data);
                var ids = new List<(int Position, string Id)>();
                foreach (Match match in IdRegex().Matches(text))
                {
                    ids.Add((match.Index, match.Groups["id"].Value));
                }

                var body = BodyRegex().Match(text);
                anchors = (ids, body.Success ? body.Index : 0);
                _anchors.Add(partIndex, anchors);
            }

            // A position at the start of a tag, or inside it, is the position of the tag
            position = Math.Clamp(position, 0, data.Length);
            var nextEnd = data.AsSpan(position).IndexOf((byte)'>');
            var nextStart = data.AsSpan(position).IndexOf((byte)'<');
            if (nextEnd >= 0 && (nextStart is 0 || nextStart < 0 || nextEnd < nextStart))
            {
                position += nextEnd + 1;
            }

            // The closest element with an id before the position, in the body
            string? result = null;
            foreach (var (tagPosition, id) in anchors.Ids)
            {
                if (tagPosition >= position)
                    break;

                result = tagPosition >= anchors.BodyStart && id.Length > 0 ? id : null;
            }

            // The id is in the form of the text the links go to: a UTF-8 text read as Latin-1
            return result;
        }
    }
}
