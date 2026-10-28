using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Encodings;
using Meziantou.Framework.WebPlatform.Html.Parsing;
using Meziantou.Framework.WebPlatform.Infra;
using Meziantou.Framework.WebPlatform.Urls;
using Meziantou.Framework.WebPlatform.WebAssembly;
using Meziantou.Framework.WebPlatform.WebAudio.Decoding;
using Meziantou.Framework.WebPlatform.WebVtt;
using WebCompression = Meziantou.Framework.WebPlatform.Compression;

namespace Meziantou.Framework.WebPlatform.Fuzz;

/// <summary>
/// The targets of the decoders of bytes: an input split into chunks must decode as the whole input does, and the
/// decoders must agree with an independent implementation where there is one (the base class library for DEFLATE, the
/// specification for the event streams).
/// </summary>
public static partial class WebPlatformFuzzTargets
{
    private static readonly WebCompression.CompressionFormat[] CompressionFormats =
    [
        WebCompression.CompressionFormat.Deflate,
        WebCompression.CompressionFormat.DeflateRaw,
        WebCompression.CompressionFormat.Gzip,
        WebCompression.CompressionFormat.Brotli,
    ];

    private static readonly string?[] TransportEncodingLabels = [null, "utf-8", "windows-1252", "shift_jis", "utf-16le", "x-unknown"];

    private static readonly string[] DefaultEncodings = ["windows-1252", "utf-8", "shift_jis", "windows-1251"];

    private static readonly float[] AudioSampleRates = [8000, 22050, 44100, 48000, 96000];

    private static readonly string[] FormDataMimeTypes =
    [
        "multipart/form-data; boundary=BOUNDARY",
        "application/x-www-form-urlencoded",
        "multipart/form-data; boundary=\"a b\"",
        "multipart/form-data",
        "text/plain",
    ];

    private static readonly string[] WasmExportNames = ["", "a", "f", "memory", "table", "inc", "\u00E9"];

    /// <summary>
    /// Decompresses the input (an option byte gives the format and the size of the chunks) in one call and in chunks, and
    /// compares the output with the decoders of the base class library. The input is also compressed and decompressed.
    /// </summary>
    public static void Inflate(ReadOnlySpan<byte> data)
    {
        var options = FuzzInput.ReadByte(ref data);
        var format = CompressionFormats[options & 3];
        var chunkSeed = options >> 2;

        var whole = Decompress(format, data, [data.Length]);
        var chunked = Decompress(format, data, GetChunkSizes(data.Length, chunkSeed));
        FuzzAssert.Equal(whole.Error, chunked.Error, "Decompressing the input in chunks fails differently");
        FuzzAssert.Equal(whole.HasTrailingInput, chunked.HasTrailingInput, "Decompressing the input in chunks finds other trailing input");
        FuzzAssert.True(whole.Output.AsSpan().SequenceEqual(chunked.Output), "Decompressing the input in chunks gives another output");

        if (whole.Error is null && !whole.HasTrailingInput && format is not WebCompression.CompressionFormat.Brotli)
        {
            byte[] expected;
            try
            {
                expected = DecompressWithBaseClassLibrary(format, data);
            }
            catch (InvalidDataException ex)
            {
                FuzzAssert.Fail($"The base class library rejects the {format} data that the decoder accepts: {ex.Message}");
                return;
            }

            FuzzAssert.True(expected.AsSpan().SequenceEqual(whole.Output), $"The {format} data decompresses to another output than with the base class library ({expected.Length} bytes, {whole.Output.Length} bytes)");
        }

        // The compressed input decompresses to the input
        byte[] compressed;
        using (var compressor = new WebCompression.Compressor(format))
        {
            compressed = [.. compressor.Compress(data), .. compressor.Finish()];
        }

        var roundTrip = Decompress(format, compressed, GetChunkSizes(compressed.Length, chunkSeed));
        FuzzAssert.Equal(null, roundTrip.Error, $"The compressed input cannot be decompressed ({format})");
        FuzzAssert.False(roundTrip.HasTrailingInput, $"The compressed input has trailing input ({format})");
        FuzzAssert.True(roundTrip.Output.AsSpan().SequenceEqual(data), $"The compressed input decompresses to another output ({format})");
    }

    private static (string? Error, byte[] Output, bool HasTrailingInput) Decompress(WebCompression.CompressionFormat format, ReadOnlySpan<byte> data, List<int> chunkSizes)
    {
        using var decompressor = WebCompression.Decompressor.Create(format);
        var output = new List<byte>();
        var hasTrailingInput = false;
        try
        {
            var position = 0;
            foreach (var size in chunkSizes)
            {
                output.AddRange(decompressor.Decompress(data.Slice(position, size)));
                hasTrailingInput |= decompressor.HasTrailingInput;
                position += size;
            }

            output.AddRange(decompressor.Finish());
            return (null, [.. output], hasTrailingInput);
        }
        catch (InvalidDataException ex)
        {
            return (ex.Message, [], false);
        }
    }

    private static byte[] DecompressWithBaseClassLibrary(WebCompression.CompressionFormat format, ReadOnlySpan<byte> data)
    {
        using var input = new MemoryStream(data.ToArray());
        using Stream stream = format switch
        {
            WebCompression.CompressionFormat.Deflate => new ZLibStream(input, CompressionMode.Decompress),
            WebCompression.CompressionFormat.DeflateRaw => new DeflateStream(input, CompressionMode.Decompress),
            _ => new GZipStream(input, CompressionMode.Decompress),
        };
        using var output = new MemoryStream();
        stream.CopyTo(output);
        return output.ToArray();
    }

    /// <summary>Splits a length into chunks whose sizes depend on a seed: the seed 0 gives a single chunk.</summary>
    private static List<int> GetChunkSizes(int length, int seed)
    {
        if (seed == 0)
            return [length];

        var sizes = new List<int>();
        for (var i = 0; length > 0; i++)
        {
            var size = Math.Min(length, 1 + ((seed * (i + 3)) % 23));
            sizes.Add(size);
            length -= size;
        }

        return sizes;
    }

    /// <summary>Reads the sections of a WebAssembly module, and looks up exports and imports.</summary>
    public static void Wasm(ReadOnlySpan<byte> data)
    {
        var sections = WasmModuleSections.Read(data);
        foreach (var (name, offset, length) in sections.CustomSections)
        {
            FuzzAssert.True(name is not null, "A custom section has no name");
            FuzzAssert.True(offset >= 8 && length >= 0 && offset + length <= data.Length, $"The custom section '{name}' ({offset}, {length}) is outside the module ({data.Length} bytes)");
        }

        foreach (var name in WasmExportNames)
        {
            _ = sections.GetExportedFunctionIndex(name);
            _ = sections.GetExportedTable(name);
            _ = sections.GetReexportedImport(name);
        }

        for (var i = 0; i < 4; i++)
        {
            _ = sections.GetImportedTable(i);
        }
    }

    /// <summary>
    /// Determines the encoding of an HTML document (an option byte gives the transport encoding, the default encoding and
    /// the chunks): the prefixes that the sniffing algorithm says determine the encoding must give the encoding of the
    /// whole document, and the encoding detector must guess the same encoding from chunks.
    /// </summary>
    public static void EncodingSniff(ReadOnlySpan<byte> data)
    {
        var options = FuzzInput.ReadByte(ref data);
        var transportEncoding = FuzzInput.Pick(TransportEncodingLabels, options);
        var defaultEncoding = FuzzInput.Pick(DefaultEncodings, (byte)(options >> 3));
        var chunkSeed = options >> 5;

        FuzzAssert.True(EncodingSniffer.TrySniff(data, isComplete: true, transportEncoding, out var expected, out var expectedLateDeclaration), "The sniffing of a whole document is not determined");
        var dataLength = data.Length;
        foreach (var length in new[] { 0, 1, 2, 3, 4, 5, 64, 512, 1023, 1024, 1025, 4096, dataLength / 2, dataLength - 1 }.Where(length => length >= 0 && length < dataLength).Distinct())
        {
            if (EncodingSniffer.TrySniff(data[..length], isComplete: false, transportEncoding, out var result, out var lateDeclaration))
            {
                FuzzAssert.Equal(expected, result, $"The first {length} bytes determine another encoding than the document");
                FuzzAssert.Equal(expectedLateDeclaration, lateDeclaration, $"The first {length} bytes determine another late declaration than the document");
            }
        }

        _ = EncodingSniffer.IsIncompleteXmlDeclaration(data);
        _ = EncodingSniffer.ExtractCharacterEncodingFromMeta(Encoding.Latin1.GetString(data));

        // The detector is fed as the bytes arrive: the chunks do not change its guess
        var whole = new LegacyEncodingDetector();
        whole.Feed(data, last: true);
        var chunked = new LegacyEncodingDetector();
        var position = 0;
        var sizes = GetChunkSizes(data.Length, chunkSeed + 1);
        for (var i = 0; i < sizes.Count; i++)
        {
            chunked.Feed(data.Slice(position, sizes[i]), last: i == sizes.Count - 1);
            position += sizes[i];
        }

        if (sizes.Count == 0)
        {
            chunked.Feed([], last: true);
        }

        FuzzAssert.Equal(whole.NonAsciiSeen, chunked.NonAsciiSeen, "The detector fed with chunks sees other bytes");
        FuzzAssert.Equal(string.Join(", ", whole.GetScores()), string.Join(", ", chunked.GetScores()), "The detector fed with chunks scores the encodings differently");
        FuzzAssert.Equal(whole.Guess(allowUtf8: true), chunked.Guess(allowUtf8: true), "The detector fed with chunks guesses another encoding");
        FuzzAssert.Equal(whole.Guess(allowUtf8: false), chunked.Guess(allowUtf8: false), "The detector fed with chunks guesses another encoding than UTF-8");

        foreach (var isComplete in new[] { true, false })
        {
            if (EncodingDetector.Default.Detect(data, new EncodingDetectionContext(url: null, isComplete)) is { } detected)
            {
                FuzzAssert.True(WebEncodingRegistry.TryGetEncoding(detected, out _), $"The detector returns the unknown encoding '{detected}'");
            }
        }

        var document = new HtmlDocumentParser(new HtmlParserOptions { TransportEncoding = transportEncoding, DefaultEncoding = defaultEncoding }).Parse(data);
        if (expected is { Encoding: { } sniffedEncoding, Confidence: EncodingConfidence.Certain } && expectedLateDeclaration is null)
        {
            FuzzAssert.Equal(sniffedEncoding, document.Encoding, "The parser does not use the encoding that the sniffing algorithm determines");
        }

        _ = document.ToHtml();
    }

    /// <summary>Parses a WebVTT file (an option byte gives the chunks) in one call and in chunks, and the text of its cues.</summary>
    public static void WebVttFile(ReadOnlySpan<byte> data)
    {
        var chunkSeed = FuzzInput.ReadByte(ref data);
        var cues = new List<VttCue>();
        var regions = new List<VttRegion>();
        var styleSheets = new List<string>();
        var isValid = WebVttParser.Parse(data, cues.Add, regions, styleSheets.Add);

        var chunkedCues = new List<VttCue>();
        var chunkedRegions = new List<VttRegion>();
        var chunkedStyleSheets = new List<string>();
        var parser = new WebVttParser(chunkedCues.Add, chunkedRegions, chunkedStyleSheets.Add);
        var position = 0;
        foreach (var size in GetChunkSizes(data.Length, chunkSeed))
        {
            parser.Append(data.Slice(position, size));
            position += size;
        }

        parser.Complete();
        FuzzAssert.Equal(isValid, !parser.HasFailed, "Parsing the file in chunks does not agree on the signature");
        FuzzAssert.Equal(string.Join("\n", cues.Select(FormatCue)), string.Join("\n", chunkedCues.Select(FormatCue)), "Parsing the file in chunks gives other cues");
        FuzzAssert.Equal(string.Join("\n", regions.Select(FormatRegion)), string.Join("\n", chunkedRegions.Select(FormatRegion)), "Parsing the file in chunks gives other regions");
        FuzzAssert.Equal(string.Join("\n", styleSheets), string.Join("\n", chunkedStyleSheets), "Parsing the file in chunks gives other style sheets");

        foreach (var cue in cues)
        {
            FuzzAssert.True(double.IsFinite(cue.StartTime) && cue.StartTime >= 0, $"The cue starts at {cue.StartTime}");
            _ = WebVttCueTextParser.Parse(cue.Text, "en");

            // A timestamp written by the serializer of the cues reads as the same time
            if (cue.StartTime < 1e9)
            {
                var timestamp = WebVttNode.FormatTimestamp(cue.StartTime);
                var timestampPosition = 0;
                FuzzAssert.Equal(cue.StartTime, WebVttParser.CollectTimestamp(timestamp, ref timestampPosition), $"The timestamp '{timestamp}' does not read as the start of the cue");
                FuzzAssert.Equal(timestamp.Length, timestampPosition, $"The timestamp '{timestamp}' is not read to its end");
            }
        }
    }

    private static string FormatCue(VttCue cue)
        => string.Create(CultureInfo.InvariantCulture, $"{cue.Id}|{cue.StartTime:R}|{cue.EndTime:R}|{cue.Vertical}|{cue.SnapToLines}|{cue.Line}|{cue.LineAlign}|{cue.Position}|{cue.PositionAlign}|{cue.Size:R}|{cue.Align}|{cue.Region?.Id}|{cue.Text}");

    private static string FormatRegion(VttRegion region)
        => string.Create(CultureInfo.InvariantCulture, $"{region.Id}|{region.Width:R}|{region.Lines}|{region.RegionAnchorX:R}|{region.RegionAnchorY:R}|{region.ViewportAnchorX:R}|{region.ViewportAnchorY:R}|{region.Scroll}");

    /// <summary>Decodes an audio file (WAV or FLAC) at a sample rate an option byte gives.</summary>
    public static void Audio(ReadOnlySpan<byte> data)
    {
        var sampleRate = FuzzInput.Pick(AudioSampleRates, FuzzInput.ReadByte(ref data));
        DecodedAudio decoded;
        try
        {
            decoded = AudioDecoder.Decode(data, sampleRate);
        }
        catch (InvalidDataException)
        {
            return;
        }

        FuzzAssert.Equal(sampleRate, decoded.SampleRate, "The audio is not resampled to the sample rate");
        FuzzAssert.True(decoded.Channels.Length is > 0 and <= 32, $"The audio has {decoded.Channels.Length} channels");
        var length = decoded.Channels[0].Length;
        FuzzAssert.True(length > 0, "The audio has no sample frame");

        // The samples of a FLAC file are integers; those of a WAV file can be floating-point numbers, NaN included
        var isFlac = !WavDecoder.IsWav(data);
        foreach (var channel in decoded.Channels)
        {
            FuzzAssert.Equal(length, channel.Length, "The channels have different lengths");
            if (isFlac)
            {
                FuzzAssert.True(Array.TrueForAll(channel, float.IsFinite), "A FLAC sample is not a finite number");
            }
        }
    }

    /// <summary>Parses the body of a request as a FormData: an option byte gives its MIME type.</summary>
    public static void FormDataBody(ReadOnlySpan<byte> data)
    {
        var mimeType = FuzzInput.Pick(FormDataMimeTypes, FuzzInput.ReadByte(ref data));
        var bytes = data.ToArray();
        FormData formData;
        try
        {
            formData = FetchBodyParsing.ParseFormData(bytes, mimeType);
        }
        catch (TypeErrorException)
        {
            FuzzAssert.True(mimeType.StartsWith("multipart/", StringComparison.Ordinal) || mimeType == "text/plain", $"A body of type {mimeType} is rejected");
            return;
        }

        foreach (var (name, value) in formData.Entries)
        {
            FuzzAssert.True(name is not null && value.Value is not null, "An entry has no name or no value");
        }

        // A urlencoded body serializes to a body that parses to the same entries
        if (mimeType == "application/x-www-form-urlencoded")
        {
            var entries = FormUrlEncoded.Parse(bytes);
            var serialized = FormUrlEncoded.Serialize(entries);
            FuzzAssert.Equal(FormatEntries(entries), FormatEntries(FormUrlEncoded.ParseString(serialized)), "The serialized entries parse to other entries");
            FuzzAssert.Equal(entries.Count, formData.Entries.Count, "The FormData does not have the entries of the body");
        }
    }

    private static string FormatEntries(List<KeyValuePair<string, string>> entries) => string.Join("&", entries.Select(entry => FuzzAssert.Format(entry.Key) + "=" + FuzzAssert.Format(entry.Value)));

    /// <summary>
    /// Parses an event stream (an option byte gives the chunks and the initial last event ID) in one call and in chunks,
    /// and compares the events with an implementation of the specification.
    /// </summary>
    public static void EventStream(ReadOnlySpan<byte> data)
    {
        var options = FuzzInput.ReadByte(ref data);
        var lastEventId = (options & 1) != 0 ? "initial" : "";
        var whole = ParseEventStream(data, [data.Length], lastEventId);
        var chunked = ParseEventStream(data, GetChunkSizes(data.Length, options >> 1), lastEventId);
        FuzzAssert.Equal(whole, chunked, "Parsing the event stream in chunks gives other events");
        FuzzAssert.Equal(ParseEventStreamWithTheSpecification(data, lastEventId), whole, "The events are not those of the specification");
    }

    private static string ParseEventStream(ReadOnlySpan<byte> data, List<int> chunkSizes, string lastEventId)
    {
        var events = new StringBuilder();
        var parser = new EventStreamParser(lastEventId, message => events.Append("event ").Append(FuzzAssert.Format(message.Type)).Append(' ').Append(FuzzAssert.Format(message.Data)).Append(' ').Append(FuzzAssert.Format(message.LastEventId)).Append('\n'), retry => events.Append("retry ").Append(retry.TotalMilliseconds.ToString(CultureInfo.InvariantCulture)).Append('\n'));
        var position = 0;
        foreach (var size in chunkSizes)
        {
            parser.Write(data.Slice(position, size));
            position += size;
        }

        events.Append("last ").Append(FuzzAssert.Format(parser.LastEventId));
        return events.ToString();
    }

    /// <summary>https://html.spec.whatwg.org/multipage/server-sent-events.html#event-stream-interpretation</summary>
    private static string ParseEventStreamWithTheSpecification(ReadOnlySpan<byte> data, string lastEventIdString)
    {
        // UTF-8 decode, without the leading byte order mark; the bytes after the last end of line are never processed
        if (data.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
        {
            data = data[3..];
        }

        var text = Encoding.UTF8.GetString(data);
        var events = new StringBuilder();
        var dataBuffer = new StringBuilder();
        var eventType = "";
        var lastEventIdBuffer = lastEventIdString;
        var position = 0;
        while (true)
        {
            var end = text.AsSpan(position).IndexOfAny('\r', '\n');
            if (end < 0)
                break;

            var line = text.Substring(position, end);
            position += end + 1;
            if (text[position - 1] == '\r' && position < text.Length && text[position] == '\n')
            {
                position++;
            }

            if (line.Length == 0)
            {
                // Dispatch the event
                lastEventIdString = lastEventIdBuffer;
                if (dataBuffer.Length > 0)
                {
                    var eventData = dataBuffer.ToString(0, dataBuffer.Length - 1);
                    events.Append("event ").Append(FuzzAssert.Format(eventType.Length == 0 ? "message" : eventType)).Append(' ').Append(FuzzAssert.Format(eventData)).Append(' ').Append(FuzzAssert.Format(lastEventIdBuffer)).Append('\n');
                }

                dataBuffer.Clear();
                eventType = "";
                continue;
            }

            if (line[0] == ':')
                continue;

            var colon = line.IndexOf(':', StringComparison.Ordinal);
            var field = colon < 0 ? line : line[..colon];
            var value = colon < 0 ? "" : line[(colon + 1)..];
            if (value.Length > 0 && value[0] == ' ')
            {
                value = value[1..];
            }

            switch (field)
            {
                case "event":
                    eventType = value;
                    break;

                case "data":
                    dataBuffer.Append(value).Append('\n');
                    break;

                case "id" when !value.Contains('\0', StringComparison.Ordinal):
                    lastEventIdBuffer = value;
                    break;

                case "retry" when value.Length > 0 && value.All(char.IsAsciiDigit):
                    var milliseconds = value.TrimStart('0').Length > 10 ? int.MaxValue : Math.Min(long.Parse(value, CultureInfo.InvariantCulture), int.MaxValue);
                    events.Append("retry ").Append(milliseconds.ToString(CultureInfo.InvariantCulture)).Append('\n');
                    break;
            }
        }

        events.Append("last ").Append(FuzzAssert.Format(lastEventIdString));
        return events.ToString();
    }

    /// <summary>Parses a MIME type and a data: URL, and parses their serializations again.</summary>
    public static void Mime(string input)
    {
        if (MimeType.Parse(input) is { } mimeType)
        {
            var serialized = mimeType.ToString();
            FuzzAssert.Equal(mimeType.Essence.ToLowerInvariant(), mimeType.Essence, "The essence of the MIME type is not lowercase");
            FuzzAssert.Equal(serialized, MimeType.Parse(serialized)?.ToString(), "The serialization of the MIME type parses to another MIME type");
            _ = mimeType.IsJavaScript;
            _ = mimeType.IsXml;
        }

        var dataUrl = input.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ? input : "data:" + input;
        if (UrlParser.Parse(dataUrl, baseUrl: null) is { } url && DataUrl.Process(url) is var (dataMimeType, _))
        {
            FuzzAssert.Equal(dataMimeType, MimeType.Parse(dataMimeType)?.ToString(), $"The MIME type '{dataMimeType}' of the data: URL is not the serialization of a MIME type");
        }
    }

    private static IEnumerable<FuzzSeed> GetInflateSeeds()
    {
        byte[][] payloads =
        [
            [],
            "a"u8.ToArray(),
            "hello hello hello hello hello"u8.ToArray(),
            Encoding.UTF8.GetBytes(string.Concat(Enumerable.Range(0, 200).Select(i => i.ToString(CultureInfo.InvariantCulture)))),
            [.. Enumerable.Range(0, 600).Select(i => (byte)((i * 7919) >> 3))],
            [.. Enumerable.Repeat((byte)0, 3000)],
        ];

        var index = 0;
        foreach (var payload in payloads)
        {
            foreach (var level in new[] { CompressionLevel.NoCompression, CompressionLevel.Fastest, CompressionLevel.SmallestSize })
            {
                for (var format = 0; format < CompressionFormats.Length; format++)
                {
                    using var output = new MemoryStream();
                    using (Stream stream = CompressionFormats[format] switch
                    {
                        WebCompression.CompressionFormat.Deflate => new ZLibStream(output, level, leaveOpen: true),
                        WebCompression.CompressionFormat.DeflateRaw => new DeflateStream(output, level, leaveOpen: true),
                        WebCompression.CompressionFormat.Gzip => new GZipStream(output, level, leaveOpen: true),
                        _ => new BrotliStream(output, level, leaveOpen: true),
                    })
                    {
                        stream.Write(payload);
                    }

                    // The chunks vary with the seeds
                    byte[] seed = [(byte)(format | ((index % 9) << 2)), .. output.ToArray()];
                    yield return new FuzzSeed($"{CompressionFormats[format]}-{level}-{index}", seed);
                    index++;
                }
            }
        }
    }

    private static IEnumerable<FuzzSeed> GetEncodingSniffSeeds()
    {
        string[] documents =
        [
            "<!DOCTYPE html><meta charset=utf-8><p>\u00E9",
            "<meta http-equiv=\"Content-Type\" content=\"text/html; charset=shift_jis\"><p>x",
            "<?xml version=\"1.0\" encoding=\"windows-1251\"?><html></html>",
            "<!-- comment --><meta charset='koi8-r'>",
            "<head><title>t</title>" + new string(' ', 1100) + "<meta charset=iso-8859-2></head><body>\u0141",
            "<meta content=\"charset=gbk\" http-equiv=\"content-type\"><body>",
            "<p>no declaration \u00E9\u00E8\u00E0</p>",
        ];

        var index = 0;
        foreach (var document in documents)
        {
            foreach (var encoding in new[] { Encoding.UTF8, Encoding.Latin1 })
            {
                yield return new FuzzSeed($"document-{index++}", (byte[])[(byte)index, .. encoding.GetBytes(document)]);
            }
        }

        // Byte order marks, and bytes of legacy encodings without declaration
        yield return new FuzzSeed("utf-8-bom", (byte[])[0, 0xEF, 0xBB, 0xBF, .. "<p>\u00E9"u8]);
        yield return new FuzzSeed("utf-16le-bom", (byte[])[1, .. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes("<p>\u00E9")]);
        yield return new FuzzSeed("utf-16be-bom", (byte[])[2, .. Encoding.BigEndianUnicode.GetPreamble(), .. Encoding.BigEndianUnicode.GetBytes("<p>\u00E9")]);
        yield return new FuzzSeed("shift_jis", (byte[])[3, .. "<p>"u8, 0x82, 0xA0, 0x82, 0xA2, 0x82, 0xA4, 0x93, 0xFA, 0x96, 0x7B]);
        yield return new FuzzSeed("windows-1251", (byte[])[4, .. "<p>"u8, 0xCF, 0xF0, 0xE8, 0xE2, 0xE5, 0xF2, 0x20, 0xEC, 0xE8, 0xF0]);
        yield return new FuzzSeed("euc-kr", (byte[])[5, .. "<p>"u8, 0xC7, 0xD1, 0xB1, 0xB9, 0xBE, 0xEE]);
        foreach (var seed in GetSeeds("html").Take(20))
        {
            yield return seed with { Name = "html-" + seed.Name, Data = (byte[])[(byte)index++, .. seed.Data.Span] };
        }
    }

    private static IEnumerable<FuzzSeed> GetAudioSeeds()
    {
        foreach (var seed in GetSeeds("audio"))
        {
            yield return seed with { Data = (byte[])[2, .. seed.Data.Span] };
        }

        // WAV files: PCM of 8, 16, 24 and 32 bits, IEEE floating-point numbers, mono and stereo
        foreach (var (format, bits, channels) in new[] { (1, 8, 1), (1, 16, 2), (1, 24, 1), (1, 32, 2), (3, 32, 1), (3, 64, 2) })
        {
            yield return new FuzzSeed($"wav-{format}-{bits}-{channels}", (byte[])[(byte)(bits + channels), .. CreateWav(format, bits, channels, sampleRate: 22050, frames: 48)]);
        }
    }

    private static byte[] CreateWav(int format, int bitsPerSample, int channels, int sampleRate, int frames)
    {
        var blockAlign = channels * bitsPerSample / 8;
        var dataLength = frames * blockAlign;
        var wav = new byte[44 + dataLength];
        "RIFF"u8.CopyTo(wav);
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(4), 36 + dataLength);
        "WAVEfmt "u8.CopyTo(wav.AsSpan(8));
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(16), 16);
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(20), (short)format);
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(22), (short)channels);
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(24), sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(28), sampleRate * blockAlign);
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(32), (short)blockAlign);
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(34), (short)bitsPerSample);
        "data"u8.CopyTo(wav.AsSpan(36));
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(40), dataLength);
        for (var i = 44; i < wav.Length; i++)
        {
            wav[i] = (byte)(i * 37);
        }

        return wav;
    }

    private static IEnumerable<FuzzSeed> GetFormDataSeeds()
    {
        (int MimeType, string Body)[] bodies =
        [
            (0, "--BOUNDARY\r\nContent-Disposition: form-data; name=\"a\"\r\n\r\nvalue\r\n--BOUNDARY\r\nContent-Disposition: form-data; name=\"file\"; filename=\"f.txt\"\r\nContent-Type: text/plain\r\n\r\ncontent\r\n--BOUNDARY--\r\n"),
            (0, "--BOUNDARY--"),
            (0, "--BOUNDARY\r\nContent-Disposition: form-data; name=\"x%22y\"\r\n\r\n\r\n--BOUNDARY--  \r\nepilogue"),
            (2, "--a b\r\nContent-Disposition: form-data; name=n\r\n\r\nv\r\n--a b--"),
            (1, "a=1&b=%C3%A9&c+d=e+f&&=&%zz=%"),
            (1, "name=\u00E9t\u00E9&x"),
            (3, "--x--"),
            (4, "a=b"),
        ];
        return bodies.Select((body, index) => new FuzzSeed($"form-data-{index}", (byte[])[(byte)body.MimeType, .. Encoding.UTF8.GetBytes(body.Body)]));
    }

    private static IEnumerable<FuzzSeed> GetEventStreamSeeds()
    {
        string[] streams =
        [
            "data: hello\n\n",
            "event: update\ndata: line 1\ndata: line 2\nid: 42\n\n",
            ": comment\r\nretry: 3000\r\ndata:x\r\n\r\n",
            "\uFEFFdata: after a byte order mark\n\n",
            "id: a\0b\ndata: id ignored\n\nid\ndata\n\n",
            "retry: 99999999999999999999\nretry: -1\nretry: 1a\n\n",
            "data: unterminated",
            "data\rdata\r\rdata: \u00E9\u4E00\U0001F600\n\n",
        ];
        return streams.Select((stream, index) => new FuzzSeed($"event-stream-{index}", (byte[])[(byte)index, .. Encoding.UTF8.GetBytes(stream)]));
    }

    private static IEnumerable<FuzzSeed> GetMimeSeeds()
    {
        var assembly = typeof(WebPlatformFuzzTargets).Assembly;
        using (var document = FuzzResources.ReadJson(assembly, "Seeds/mime-types.json"))
        {
            var index = 0;
            foreach (var test in document.RootElement.EnumerateArray().Where(test => test.ValueKind is JsonValueKind.Object))
            {
                yield return FuzzSeed.FromText($"mime-{index++}", test.GetProperty("input").GetString()!);
            }
        }

        using (var document = FuzzResources.ReadJson(assembly, "Seeds/data-urls.json"))
        {
            var index = 0;
            foreach (var test in document.RootElement.EnumerateArray().Where(test => test.ValueKind is JsonValueKind.Array))
            {
                yield return FuzzSeed.FromText($"data-url-{index++}", test[0].GetString()!);
            }
        }
    }
}
