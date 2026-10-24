using System.Globalization;
using System.Text;

namespace Meziantou.Framework.Fonts;

/// <summary>
/// Reads AFM files: Adobe Technical Note #5004, "Adobe Font Metrics File Format Specification", version 4.1.
/// </summary>
/// <remarks>
/// The file is line-oriented: each line is a key and its values, separated by spaces or tabs (section 3). The
/// sections are delimited by <c>Start...</c> and <c>End...</c> keys (section 4). The keys the reader does not know
/// are ignored, as section 3 requires of parsers. The counts that follow the <c>Start...</c> keys only announce what
/// follows: they are not checked (files get them wrong) and nothing is sized from them, so that the memory stays in
/// proportion with the text.
/// </remarks>
internal sealed class AfmParser
{
    private const int MaxTokenInMessage = 32;

    private static ReadOnlySpan<byte> Utf8Bom => [0xEF, 0xBB, 0xBF];

    private readonly Dictionary<string, string> _names = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string>.AlternateLookup<ReadOnlySpan<char>> _nameLookup;
    private readonly List<string> _comments = [];
    private readonly List<AfmCharacterMetrics> _characters = [];
    private readonly List<AfmKerningPair> _kerningPairs = [];
    private readonly List<AfmKerningPair> _verticalKerningPairs = [];
    private readonly List<AfmTrackKerning> _tracks = [];
    private readonly List<AfmCompositeCharacter> _composites = [];
    private readonly DirectionBuilder _horizontal = new();
    private readonly DirectionBuilder _vertical = new();
    private int _line;

    private string? _fontName;
    private string? _fullName;
    private string? _familyName;
    private string? _weight;
    private string? _version;
    private string? _notice;
    private string? _encodingScheme;
    private string? _characterSet;
    private int? _mappingScheme;
    private int? _escChar;
    private int? _characterCount;
    private bool? _isBaseFont;
    private bool? _isCIDFont;
    private AfmVector? _vVector;
    private bool? _isFixedV;
    private AfmBox? _fontBBox;
    private double? _capHeight;
    private double? _xHeight;
    private double? _ascender;
    private double? _descender;
    private double? _stdHW;
    private double? _stdVW;
    private int? _metricsSets;

    private AfmParser()
    {
        _nameLookup = _names.GetAlternateLookup<ReadOnlySpan<char>>();
    }

    private enum Section
    {
        Header,
        Direction,
        CharMetrics,
        KernData,
        TrackKern,
        KernPairs,
        Composites,
    }

    /// <summary>Gets a value indicating whether data starts with the first key of an AFM, AMFM or ACFM file.</summary>
    public static bool HasAfmSignature(ReadOnlySpan<byte> data)
    {
        if (data.StartsWith(Utf8Bom))
            data = data[Utf8Bom.Length..];

        return data.StartsWith("StartFontMetrics"u8) || data.StartsWith("StartMasterFontMetrics"u8) || data.StartsWith("StartCompFontMetrics"u8);
    }

    /// <summary>Reads an AFM file from its bytes.</summary>
    public static AfmFontMetrics Parse(ReadOnlySpan<byte> data)
    {
        // Section 3 allows ASCII only, but the notices of some files have characters of ISO-8859-1. A byte order mark
        // is not part of the format: an editor added it.
        if (data.StartsWith(Utf8Bom))
            data = data[Utf8Bom.Length..];

        return Parse(Encoding.Latin1.GetString(data));
    }

    /// <summary>Reads an AFM file from its text.</summary>
    public static AfmFontMetrics Parse(ReadOnlySpan<char> text) => new AfmParser().Read(text);

    private AfmFontMetrics Read(ReadOnlySpan<char> text)
    {
        // Section 4: "The StartFontMetrics keyword must be the first line in the file".
        _line = 1;
        var lines = new LineReader(text);
        if (!lines.TryRead(out var first))
            throw Invalid("the file is empty.");

        SplitKey(TrimWhitespace(first), out var firstKey, out var formatVersion);
        switch (firstKey)
        {
            case "StartFontMetrics":
                break;

            case "StartMasterFontMetrics":
                throw FontErrors.Unsupported("The file is the AMFM file of a multiple master font, which is not supported: read the AFM files of its master designs.", "AMFM");

            case "StartCompFontMetrics":
                throw FontErrors.Unsupported("The file is the ACFM file of a composite font, which is not supported.", "ACFM");

            default:
                throw Invalid("the file does not start with StartFontMetrics.");
        }

        if (formatVersion.IsEmpty)
            throw Invalid("StartFontMetrics has no version.");

        var section = Section.Header;
        var kernPairsParent = Section.Header;
        var trackKernParent = Section.Header;
        var kernPairsVertical = false;
        var directions = DirectionSet.Horizontal;
        var ended = false;
        while (lines.TryRead(out var rawLine))
        {
            _line++;
            var line = TrimWhitespace(rawLine);

            // Section 3: empty lines, or lines of white space only, can occur anywhere after the first line.
            if (line.IsEmpty)
                continue;

            SplitKey(line, out var key, out var rest);
            var consumed = false;

            // Section 3.1: comments can be anywhere, the tracks of section 11.1 have some.
            if (key is "Comment")
            {
                _comments.Add(rest.ToString());
                continue;
            }

            switch (section)
            {
                case Section.Header or Section.Direction:
                    switch (key)
                    {
                        case "EndFontMetrics" when section is Section.Header:
                            ended = true;
                            consumed = true;
                            break;

                        case "StartDirection" when section is Section.Header:
                            // Section 7.2: StartDirection 2 means that both directions have the same metrics.
                            var direction = ReadInteger(rest, key);
                            directions = direction switch
                            {
                                0 => DirectionSet.Horizontal,
                                1 => DirectionSet.Vertical,
                                2 => DirectionSet.Both,
                                _ => throw Invalid($"StartDirection has the direction {direction}, which is not 0, 1 or 2."),
                            };

                            _horizontal.Described |= direction is 0 or 2;
                            _vertical.Described |= direction is 1 or 2;
                            section = Section.Direction;
                            consumed = true;
                            break;

                        case "EndDirection" when section is Section.Direction:
                            // Section 7.2: outside StartDirection, the metrics are those of direction 0.
                            directions = DirectionSet.Horizontal;
                            section = Section.Header;
                            consumed = true;
                            break;

                        case "StartCharMetrics" when section is Section.Header:
                            section = Section.CharMetrics;
                            consumed = true;
                            break;

                        case "StartKernData" when section is Section.Header:
                            section = Section.KernData;
                            consumed = true;
                            break;

                        // Section 9 puts the kerning in StartKernData; files without it are read too.
                        case "StartKernPairs" or "StartKernPairs0" or "StartKernPairs1" when section is Section.Header:
                            kernPairsParent = section;
                            kernPairsVertical = key is "StartKernPairs1";
                            section = Section.KernPairs;
                            consumed = true;
                            break;

                        case "StartTrackKern" when section is Section.Header:
                            trackKernParent = section;
                            section = Section.TrackKern;
                            consumed = true;
                            break;

                        case "StartComposites" when section is Section.Header:
                            section = Section.Composites;
                            consumed = true;
                            break;

                        default:
                            ReadGlobalKey(key, rest, directions);
                            break;
                    }

                    break;

                case Section.CharMetrics:
                    if (key is "EndCharMetrics")
                    {
                        section = Section.Header;
                        consumed = true;
                    }
                    else if (!IsDelimiter(key))
                    {
                        ReadCharacterMetrics(line);
                    }

                    break;

                case Section.KernData:
                    switch (key)
                    {
                        case "EndKernData":
                            section = Section.Header;
                            consumed = true;
                            break;

                        case "StartKernPairs" or "StartKernPairs0" or "StartKernPairs1":
                            kernPairsParent = section;
                            kernPairsVertical = key is "StartKernPairs1";
                            section = Section.KernPairs;
                            consumed = true;
                            break;

                        case "StartTrackKern":
                            trackKernParent = section;
                            section = Section.TrackKern;
                            consumed = true;
                            break;
                    }

                    break;

                case Section.TrackKern:
                    switch (key)
                    {
                        case "EndTrackKern":
                            section = trackKernParent;
                            consumed = true;
                            break;

                        case "TrackKern":
                            ReadTrackKern(rest);
                            break;
                    }

                    break;

                case Section.KernPairs:
                    if (key is "EndKernPairs")
                    {
                        section = kernPairsParent;
                        consumed = true;
                    }
                    else
                    {
                        ReadKerningPair(key, rest, kernPairsVertical ? _verticalKerningPairs : _kerningPairs);
                    }

                    break;

                case Section.Composites:
                    if (key is "EndComposites")
                    {
                        section = Section.Header;
                        consumed = true;
                    }
                    else if (!IsDelimiter(key))
                    {
                        ReadComposite(line);
                    }

                    break;
            }

            // A delimiter that does not belong to the current section is a structure error, not an unknown key.
            if (!consumed && IsDelimiter(key))
                throw Invalid($"{key} is not expected in the {GetSectionName(section)} section.");

            if (ended)
                break;
        }

        // Section 4: "EndFontMetrics keyword must be the last non-empty line in the file". What follows it is ignored:
        // some files end with an end-of-file character.
        if (!ended)
        {
            if (section is not Section.Header)
                throw Invalid($"the {GetSectionName(section)} section is not closed.");

            throw Invalid("EndFontMetrics is missing.");
        }

        return new AfmFontMetrics(formatVersion.ToString(), _characters, _kerningPairs, _verticalKerningPairs)
        {
            FontName = _fontName,
            FullName = _fullName,
            FamilyName = _familyName,
            Weight = _weight,
            Version = _version,
            Notice = _notice,
            EncodingScheme = _encodingScheme,
            CharacterSet = _characterSet,
            Comments = _comments,
            MappingScheme = _mappingScheme,
            EscChar = _escChar,
            CharacterCount = _characterCount,

            // Section 7.1: "If not present, this is assumed to be a base font program".
            IsBaseFont = _isBaseFont ?? true,
            IsCIDFont = _isCIDFont ?? false,
            VVector = _vVector,

            // Section 7.1: "if absent, its value is assumed to be true if VVector is present".
            IsFixedV = _isFixedV ?? _vVector is not null,
            FontBBox = _fontBBox,
            CapHeight = _capHeight,
            XHeight = _xHeight,
            Ascender = _ascender,
            Descender = _descender,
            StdHW = _stdHW,
            StdVW = _stdVW,

            // Section 4.1: "If not present, MetricsSets 0 is implied".
            MetricsSets = _metricsSets ?? 0,
            Horizontal = _horizontal.Described || _metricsSets is null or 0 or 2 ? _horizontal.Build() : null,
            Vertical = _vertical.Described || _metricsSets is 1 or 2 ? _vertical.Build() : null,
            TrackKerning = _tracks,
            Composites = _composites,
        };
    }

    // The keys of sections 4.1, 4.2 and 4.3 (defined in sections 7.1 and 7.2).
    private void ReadGlobalKey(string key, ReadOnlySpan<char> rest, DirectionSet directions)
    {
        switch (key)
        {
            case "FontName":
                _fontName = rest.ToString();
                break;

            case "FullName":
                _fullName = rest.ToString();
                break;

            case "FamilyName":
                _familyName = rest.ToString();
                break;

            case "Weight":
                _weight = rest.ToString();
                break;

            case "Version":
                _version = rest.ToString();
                break;

            case "Notice":
                _notice = rest.ToString();
                break;

            case "EncodingScheme":
                _encodingScheme = rest.ToString();
                break;

            case "CharacterSet":
                _characterSet = rest.ToString();
                break;

            case "FontBBox":
                _fontBBox = ReadBox(rest, key);
                break;

            case "MappingScheme":
                _mappingScheme = ReadInteger(rest, key);
                break;

            case "EscChar":
                _escChar = ReadInteger(rest, key);
                break;

            case "Characters":
                _characterCount = ReadInteger(rest, key);
                break;

            case "IsBaseFont":
                _isBaseFont = ReadBoolean(rest, key);
                break;

            case "VVector":
                _vVector = ReadVector(rest, key);
                break;

            case "IsFixedV":
                _isFixedV = ReadBoolean(rest, key);
                break;

            case "IsCIDFont":
                _isCIDFont = ReadBoolean(rest, key);
                break;

            case "CapHeight":
                _capHeight = ReadNumber(rest, key);
                break;

            case "XHeight":
                _xHeight = ReadNumber(rest, key);
                break;

            case "Ascender":
                _ascender = ReadNumber(rest, key);
                break;

            case "Descender":
                _descender = ReadNumber(rest, key);
                break;

            case "StdHW":
                _stdHW = ReadNumber(rest, key);
                break;

            case "StdVW":
                _stdVW = ReadNumber(rest, key);
                break;

            case "MetricsSets":
                var metricsSets = ReadInteger(rest, key);
                if (metricsSets is not (0 or 1 or 2))
                    throw Invalid($"MetricsSets is {metricsSets}, which is not 0, 1 or 2.");

                _metricsSets = metricsSets;
                break;

            case "UnderlinePosition":
                var underlinePosition = ReadNumber(rest, key);
                ForEachDirection(directions, builder => builder.UnderlinePosition = underlinePosition);
                break;

            case "UnderlineThickness":
                var underlineThickness = ReadNumber(rest, key);
                ForEachDirection(directions, builder => builder.UnderlineThickness = underlineThickness);
                break;

            case "ItalicAngle":
                var italicAngle = ReadNumber(rest, key);
                ForEachDirection(directions, builder => builder.ItalicAngle = italicAngle);
                break;

            case "CharWidth":
                var charWidth = ReadVector(rest, key);
                ForEachDirection(directions, builder => builder.CharWidth = charWidth);
                break;

            case "IsFixedPitch":
                var isFixedPitch = ReadBoolean(rest, key);
                ForEachDirection(directions, builder => builder.IsFixedPitch = isFixedPitch);
                break;
        }
    }

    private void ForEachDirection(DirectionSet directions, Action<DirectionBuilder> action)
    {
        if (directions is DirectionSet.Horizontal or DirectionSet.Both)
        {
            _horizontal.Described = true;
            action(_horizontal);
        }

        if (directions is DirectionSet.Vertical or DirectionSet.Both)
        {
            _vertical.Described = true;
            action(_vertical);
        }
    }

    // Section 8: "C 102 ; WX 333 ; N f ; B 20 0 383 682 ; L i fi ; L l fl ;", in any order.
    private void ReadCharacterMetrics(ReadOnlySpan<char> line)
    {
        int? code = null;
        string? name = null;
        double? width0X = null;
        double? width0Y = null;
        double? width1X = null;
        double? width1Y = null;
        AfmVector? vVector = null;
        AfmBox? boundingBox = null;
        List<AfmLigature>? ligatures = null;
        var hasKnownKey = false;
        foreach (var range in line.Split(';'))
        {
            var part = TrimWhitespace(line[range]);
            if (part.IsEmpty)
                continue;

            SplitKey(part, out var key, out var values);
            var known = true;
            switch (key)
            {
                case "C":
                    code = ReadInteger(values, key);
                    break;

                case "CH":
                    code = ReadHex(values, key);
                    break;

                case "WX" or "W0X":
                    width0X = ReadNumber(values, key);
                    break;

                case "WY" or "W0Y":
                    width0Y = ReadNumber(values, key);
                    break;

                case "W" or "W0":
                    var width0 = ReadVector(values, key);
                    (width0X, width0Y) = (width0.X, width0.Y);
                    break;

                case "W1X":
                    width1X = ReadNumber(values, key);
                    break;

                case "W1Y":
                    width1Y = ReadNumber(values, key);
                    break;

                case "W1":
                    var width1 = ReadVector(values, key);
                    (width1X, width1Y) = (width1.X, width1.Y);
                    break;

                case "VV":
                    vVector = ReadVector(values, key);
                    break;

                case "N":
                    name = ReadName(values, key);
                    break;

                case "B":
                    boundingBox = ReadBox(values, key);
                    break;

                case "L":
                    var tokens = new Tokens(values);
                    var successor = ReadName(ref tokens, key);
                    var ligature = ReadName(ref tokens, key);
                    (ligatures ??= []).Add(new AfmLigature(successor, ligature));
                    break;

                default:
                    known = false;
                    break;
            }

            hasKnownKey |= known;
        }

        // A line of keys the reader does not know is ignored; a line of metrics is about a character code.
        if (code is null)
        {
            if (hasKnownKey)
                throw Invalid("the metrics of a character have no code (C or CH).");

            return;
        }

        AfmVector? width = width0X is null && width0Y is null ? null : new AfmVector(width0X ?? 0, width0Y ?? 0);
        AfmVector? verticalWidth = width1X is null && width1Y is null ? null : new AfmVector(width1X ?? 0, width1Y ?? 0);
        _characters.Add(new AfmCharacterMetrics(code.Value, name, width, verticalWidth, vVector, boundingBox, ligatures is null ? [] : ligatures));
    }

    // Section 9.2.
    private void ReadKerningPair(string key, ReadOnlySpan<char> rest, List<AfmKerningPair> pairs)
    {
        var tokens = new Tokens(rest);
        switch (key)
        {
            case "KP":
                {
                    var first = ReadName(ref tokens, key);
                    var second = ReadName(ref tokens, key);
                    var x = ReadNumber(ref tokens, key);
                    var y = ReadNumber(ref tokens, key);
                    pairs.Add(new AfmKerningPair(first, second, null, null, x, y));
                    break;
                }

            case "KPX":
                {
                    var first = ReadName(ref tokens, key);
                    var second = ReadName(ref tokens, key);
                    pairs.Add(new AfmKerningPair(first, second, null, null, ReadNumber(ref tokens, key), 0));
                    break;
                }

            case "KPY":
                {
                    var first = ReadName(ref tokens, key);
                    var second = ReadName(ref tokens, key);
                    pairs.Add(new AfmKerningPair(first, second, null, null, 0, ReadNumber(ref tokens, key)));
                    break;
                }

            case "KPH":
                {
                    // "the byte strings needed to generate the characters are given, rather than the names": the codes
                    // of section 8 (CH).
                    var first = ReadHex(ref tokens, key);
                    var second = ReadHex(ref tokens, key);
                    var x = ReadNumber(ref tokens, key);
                    var y = ReadNumber(ref tokens, key);
                    pairs.Add(new AfmKerningPair(null, null, first, second, x, y));
                    break;
                }
        }
    }

    // Section 9.1: TrackKern degree min-ptsize min-kern max-ptsize max-kern.
    private void ReadTrackKern(ReadOnlySpan<char> rest)
    {
        const string Key = "TrackKern";
        var tokens = new Tokens(rest);
        var degree = ReadInteger(ref tokens, Key);
        var minPointSize = ReadNumber(ref tokens, Key);
        var minKern = ReadNumber(ref tokens, Key);
        var maxPointSize = ReadNumber(ref tokens, Key);
        var maxKern = ReadNumber(ref tokens, Key);
        _tracks.Add(new AfmTrackKerning(degree, minPointSize, minKern, maxPointSize, maxKern));
    }

    // Section 10: "CC Aacute 2 ; PCC A 0 0 ; PCC acute 195 214 ;".
    private void ReadComposite(ReadOnlySpan<char> line)
    {
        string? name = null;
        List<AfmCompositePart>? parts = null;
        foreach (var range in line.Split(';'))
        {
            var part = TrimWhitespace(line[range]);
            if (part.IsEmpty)
                continue;

            SplitKey(part, out var key, out var values);
            var tokens = new Tokens(values);
            switch (key)
            {
                case "CC" when name is null:
                    name = ReadName(ref tokens, key);

                    // The number of parts only announces the PCC keys that follow.
                    _ = ReadInteger(ref tokens, key);
                    parts = [];
                    break;

                case "PCC":
                    if (parts is null)
                        throw Invalid("PCC comes before the CC key of its composite character.");

                    var partName = ReadName(ref tokens, key);
                    var deltaX = ReadNumber(ref tokens, key);
                    var deltaY = ReadNumber(ref tokens, key);
                    parts.Add(new AfmCompositePart(partName, deltaX, deltaY));
                    break;
            }
        }

        // A line of keys the reader does not know is ignored.
        if (name is not null)
            _composites.Add(new AfmCompositeCharacter(name, parts!));
    }

    private static bool IsDelimiter(string key) => key is "EndFontMetrics" or "StartDirection" or "EndDirection" or "StartCharMetrics" or "EndCharMetrics"
        or "StartKernData" or "EndKernData" or "StartTrackKern" or "EndTrackKern" or "StartKernPairs" or "StartKernPairs0" or "StartKernPairs1" or "EndKernPairs"
        or "StartComposites" or "EndComposites";

    private static string GetSectionName(Section section) => section switch
    {
        Section.Header => "StartFontMetrics",
        Section.Direction => "StartDirection",
        Section.CharMetrics => "StartCharMetrics",
        Section.KernData => "StartKernData",
        Section.TrackKern => "StartTrackKern",
        Section.KernPairs => "StartKernPairs",
        Section.Composites => "StartComposites",
        _ => throw new ArgumentOutOfRangeException(nameof(section)),
    };

    private string ReadName(ReadOnlySpan<char> values, string key)
    {
        var tokens = new Tokens(values);
        return ReadName(ref tokens, key);
    }

    // The names of a file repeat (a character, its kerning pairs, its ligatures, its composites): one string each.
    private string ReadName(ref Tokens tokens, string key)
    {
        var token = Next(ref tokens, key);
        if (_nameLookup.TryGetValue(token, out var name))
            return name;

        name = token.ToString();
        _names.Add(name, name);
        return name;
    }

    private double ReadNumber(ReadOnlySpan<char> values, string key)
    {
        var tokens = new Tokens(values);
        return ReadNumber(ref tokens, key);
    }

    // Section 3: "A number can be either a real number or an integer, and signed or unsigned". The values are bounded
    // like the coordinates of the outlines of the other formats: nothing that is not a number leaves the reader.
    private double ReadNumber(ref Tokens tokens, string key)
    {
        var token = Next(ref tokens, key);
        if (!double.TryParse(token, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out var value) || !(Math.Abs(value) <= FontErrors.MaxCoordinate))
            throw Invalid($"the value '{Describe(token)}' of {key} is not a number in range.");

        return value;
    }

    private int ReadInteger(ReadOnlySpan<char> values, string key)
    {
        var tokens = new Tokens(values);
        return ReadInteger(ref tokens, key);
    }

    private int ReadInteger(ref Tokens tokens, string key)
    {
        var token = Next(ref tokens, key);
        if (!int.TryParse(token, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value))
            throw Invalid($"the value '{Describe(token)}' of {key} is not an integer.");

        return value;
    }

    private bool ReadBoolean(ReadOnlySpan<char> values, string key)
    {
        var tokens = new Tokens(values);
        var token = Next(ref tokens, key);
        return token switch
        {
            "true" => true,
            "false" => false,
            _ => throw Invalid($"the value '{Describe(token)}' of {key} is not true or false."),
        };
    }

    private int ReadHex(ReadOnlySpan<char> values, string key)
    {
        var tokens = new Tokens(values);
        return ReadHex(ref tokens, key);
    }

    // Section 8: "CH <hex>", for example <84AF>: the bytes of the code, most significant first.
    private int ReadHex(ref Tokens tokens, string key)
    {
        var token = Next(ref tokens, key);
        if (token.Length < 3 || token[0] != '<' || token[^1] != '>' || token.Length > 10
            || !uint.TryParse(token[1..^1], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var value) || value > int.MaxValue)
        {
            throw Invalid($"the value '{Describe(token)}' of {key} is not a hexadecimal code between angle brackets.");
        }

        return (int)value;
    }

    private AfmVector ReadVector(ReadOnlySpan<char> values, string key)
    {
        var tokens = new Tokens(values);
        var x = ReadNumber(ref tokens, key);
        var y = ReadNumber(ref tokens, key);
        return new AfmVector(x, y);
    }

    private AfmBox ReadBox(ReadOnlySpan<char> values, string key)
    {
        var tokens = new Tokens(values);
        var left = ReadNumber(ref tokens, key);
        var bottom = ReadNumber(ref tokens, key);
        var right = ReadNumber(ref tokens, key);
        var top = ReadNumber(ref tokens, key);
        return new AfmBox(left, bottom, right, top);
    }

    private ReadOnlySpan<char> Next(ref Tokens tokens, string key)
    {
        if (!tokens.TryNext(out var token))
            throw Invalid($"{key} has fewer values than it needs.");

        return token;
    }

    private InvalidFontException Invalid(string message) => FontErrors.InvalidAfm(_line, message);

    private static string Describe(ReadOnlySpan<char> token) => token.Length <= MaxTokenInMessage ? token.ToString() : string.Concat(token[..MaxTokenInMessage], "...");

    // Section 3: "White space characters are ASCII space, newline, and tab".
    private static ReadOnlySpan<char> TrimWhitespace(ReadOnlySpan<char> value) => value.Trim(" \t");

    private static void SplitKey(ReadOnlySpan<char> line, out string key, out ReadOnlySpan<char> rest)
    {
        var end = line.IndexOfAny(' ', '\t');
        var keySpan = end < 0 ? line : line[..end];
        rest = end < 0 ? [] : TrimWhitespace(line[end..]);
        key = GetKey(keySpan);
    }

    // The keys the reader knows are constants: a key it does not know becomes an empty string, which matches none.
    private static string GetKey(ReadOnlySpan<char> key) => key switch
    {
        "Comment" => "Comment",
        "StartFontMetrics" => "StartFontMetrics",
        "StartMasterFontMetrics" => "StartMasterFontMetrics",
        "StartCompFontMetrics" => "StartCompFontMetrics",
        "EndFontMetrics" => "EndFontMetrics",
        "MetricsSets" => "MetricsSets",
        "FontName" => "FontName",
        "FullName" => "FullName",
        "FamilyName" => "FamilyName",
        "Weight" => "Weight",
        "FontBBox" => "FontBBox",
        "Version" => "Version",
        "Notice" => "Notice",
        "EncodingScheme" => "EncodingScheme",
        "MappingScheme" => "MappingScheme",
        "EscChar" => "EscChar",
        "CharacterSet" => "CharacterSet",
        "Characters" => "Characters",
        "IsBaseFont" => "IsBaseFont",
        "VVector" => "VVector",
        "IsFixedV" => "IsFixedV",
        "IsCIDFont" => "IsCIDFont",
        "CapHeight" => "CapHeight",
        "XHeight" => "XHeight",
        "Ascender" => "Ascender",
        "Descender" => "Descender",
        "StdHW" => "StdHW",
        "StdVW" => "StdVW",
        "StartDirection" => "StartDirection",
        "EndDirection" => "EndDirection",
        "UnderlinePosition" => "UnderlinePosition",
        "UnderlineThickness" => "UnderlineThickness",
        "ItalicAngle" => "ItalicAngle",
        "CharWidth" => "CharWidth",
        "IsFixedPitch" => "IsFixedPitch",
        "StartCharMetrics" => "StartCharMetrics",
        "EndCharMetrics" => "EndCharMetrics",
        "C" => "C",
        "CH" => "CH",
        "WX" => "WX",
        "W0X" => "W0X",
        "W1X" => "W1X",
        "WY" => "WY",
        "W0Y" => "W0Y",
        "W1Y" => "W1Y",
        "W" => "W",
        "W0" => "W0",
        "W1" => "W1",
        "VV" => "VV",
        "N" => "N",
        "B" => "B",
        "L" => "L",
        "StartKernData" => "StartKernData",
        "EndKernData" => "EndKernData",
        "StartTrackKern" => "StartTrackKern",
        "EndTrackKern" => "EndTrackKern",
        "TrackKern" => "TrackKern",
        "StartKernPairs" => "StartKernPairs",
        "StartKernPairs0" => "StartKernPairs0",
        "StartKernPairs1" => "StartKernPairs1",
        "EndKernPairs" => "EndKernPairs",
        "KP" => "KP",
        "KPH" => "KPH",
        "KPX" => "KPX",
        "KPY" => "KPY",
        "StartComposites" => "StartComposites",
        "EndComposites" => "EndComposites",
        "CC" => "CC",
        "PCC" => "PCC",
        _ => "",
    };

    private enum DirectionSet
    {
        Horizontal,
        Vertical,
        Both,
    }

    private sealed class DirectionBuilder
    {
        public bool Described { get; set; }

        public double? UnderlinePosition { get; set; }

        public double? UnderlineThickness { get; set; }

        public double? ItalicAngle { get; set; }

        public AfmVector? CharWidth { get; set; }

        public bool? IsFixedPitch { get; set; }

        public AfmWritingDirection Build() => new(UnderlinePosition, UnderlineThickness, ItalicAngle, CharWidth, IsFixedPitch);
    }

    // The lines of the text: a line ends at a carriage return, a line feed, or both.
    private ref struct LineReader(ReadOnlySpan<char> text)
    {
        private ReadOnlySpan<char> _remaining = text;
        private bool _done = text.IsEmpty;

        public bool TryRead(out ReadOnlySpan<char> line)
        {
            if (_done)
            {
                line = default;
                return false;
            }

            var end = _remaining.IndexOfAny('\r', '\n');
            if (end < 0)
            {
                line = _remaining;
                _done = true;
                return true;
            }

            line = _remaining[..end];
            var next = end + 1;
            if (_remaining[end] == '\r' && next < _remaining.Length && _remaining[next] == '\n')
                next++;

            _remaining = _remaining[next..];
            if (_remaining.IsEmpty)
                _done = true;

            return true;
        }
    }

    // The values of a key, separated by spaces or tabs.
    private ref struct Tokens(ReadOnlySpan<char> values)
    {
        private ReadOnlySpan<char> _remaining = values;

        public bool TryNext(out ReadOnlySpan<char> token)
        {
            _remaining = _remaining.TrimStart(" \t");
            if (_remaining.IsEmpty)
            {
                token = default;
                return false;
            }

            var end = _remaining.IndexOfAny(' ', '\t');
            token = end < 0 ? _remaining : _remaining[..end];
            _remaining = end < 0 ? [] : _remaining[end..];
            return true;
        }
    }
}
