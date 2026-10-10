using Meziantou.Framework.Imaging.CorpusGenerator.Infrastructure;

namespace Meziantou.Framework.Imaging.CorpusGenerator.Common;

/// <summary>One scan header: component ids, Ss, Se, Ah, Al.</summary>
internal sealed record JpegScan(IReadOnlyList<int> Components, int Start, int End, int High, int Low);

/// <summary>Parsed JPEG stream (jpeg_inspect).</summary>
internal sealed class JpegInfo
{
    public int Scans { get; set; }

    public int Restart { get; set; }

    public List<int> ScanComponents { get; } = [];

    public List<JpegScan> ScanParameters { get; } = [];

    public bool Quantization16 { get; set; }

    public int Precision { get; set; } = 8;

    public int Sof { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }

    /// <summary>(id, horizontal sampling, vertical sampling) of each frame component.</summary>
    public List<(int Id, int H, int V)> Components { get; set; } = [];
}

internal static partial class CommonCorpus
{
    // -----------------------------------------------------------------------------------------------------------------
    // JPEG inspection
    // -----------------------------------------------------------------------------------------------------------------

    public static (JpegInfo Info, List<string> Features) JpegInspect(byte[] data)
    {
        Py.Assert(data[0] == 0xFF && data[1] == 0xD8);
        var offset = 2;
        var features = new List<string>();
        var info = new JpegInfo();
        while (offset < data.Length)
        {
            Py.Assert(data[offset] == 0xFF);
            var marker = data[offset + 1];
            offset += 2;
            if (marker == 0xD9)
                break;
            var length = Bytes.U16BE(data, offset);
            var body = Bytes.Slice(data, offset + 2, offset + length);
            if (marker is >= 0xC0 and <= 0xCF and not (0xC4 or 0xC8 or 0xCC))
            {
                info.Sof = marker;
                info.Precision = body[0];
                info.Height = Bytes.U16BE(body, 1);
                info.Width = Bytes.U16BE(body, 3);
                var count = body[5];
                info.Components = [.. Enumerable.Range(0, count).Select(i => ((int)body[6 + 3 * i], body[7 + 3 * i] >> 4, body[7 + 3 * i] & 15))];
            }
            else if (marker == 0xDB)
            {
                var table = body;
                while (table.Length > 0)
                {
                    info.Quantization16 |= (table[0] >> 4) == 1;
                    table = Bytes.Slice(table, 1 + 64 * ((table[0] >> 4) + 1));
                }
            }
            else if (marker == 0xDD)
            {
                info.Restart = Bytes.U16BE(body, 0);
            }
            else if (marker is >= 0xE0 and <= 0xEF)
            {
                var end = Array.IndexOf(body, (byte)0);
                var name = end >= 0 ? body[..end] : body;
                features.Add($"jpeg.app{Str(marker - 0xE0)}={DecodeAsciiReplace(name)}");
            }

            offset += length;
            if (marker == 0xDA)
            {
                info.Scans++;
                info.ScanComponents.Add(body[0]);
                var n = body[0];
                info.ScanParameters.Add(new JpegScan([.. Enumerable.Range(0, n).Select(i => (int)body[1 + 2 * i])], body[1 + 2 * n], body[2 + 2 * n], body[3 + 2 * n] >> 4, body[3 + 2 * n] & 15));
                while (true)
                {
                    // skip entropy-coded data (stuffed bytes and restart markers)
                    if (data[offset] == 0xFF && data[offset + 1] != 0x00 && !(data[offset + 1] is >= 0xD0 and <= 0xD7))
                        break;
                    offset++;
                }
            }
        }

        var sofName = info.Sof switch
        {
            0xC0 => "baseline",
            0xC1 => "extended",
            0xC2 => "progressive",
            0xC3 => "lossless",
            0xC9 => "arithmetic-sequential",
            0xCA => "arithmetic-progressive",
            _ => "sof-0x" + info.Sof.ToString("X2", System.Globalization.CultureInfo.InvariantCulture),
        };
        features.Add("jpeg.process=" + sofName);
        if (info.Precision != 8)
            features.Add("jpeg.precision=" + Str(info.Precision));
        var components = info.Components;
        features.Add("jpeg.components=" + Str(components.Count));
        if (components.Count == 3)
        {
            var (h0, v0) = (components[0].H, components[0].V);
            string sampling;
            if (components.Skip(1).All(c => (c.H, c.V) == (1, 1)))
            {
                sampling = (h0, v0) switch
                {
                    (1, 1) => "4:4:4",
                    (2, 1) => "4:2:2",
                    (2, 2) => "4:2:0",
                    (1, 2) => "4:4:0",
                    (4, 1) => "4:1:1",
                    _ => $"{Str(h0)}x{Str(v0)}",
                };
            }
            else
            {
                sampling = string.Join(',', components.Select(c => $"{Str(c.H)}x{Str(c.V)}"));
            }

            features.Add("jpeg.sampling=" + sampling);
        }

        features.Add("jpeg.scans=" + Str(info.Scans));
        if (info.Scans > 1 && info.Sof is 0xC0 or 0xC1)
            features.Add("jpeg.scanComponents=" + string.Join(',', info.ScanComponents));
        if (info.Sof == 0xC2)
            features.AddRange(ProgressiveFeatures(info));
        if (info.Quantization16)
            features.Add("jpeg.quantization=16-bit");
        if (info.Restart != 0)
            features.Add("jpeg.restartInterval=" + Str(info.Restart));
        return (info, [.. features.Distinct().Order(StringComparer.Ordinal)]);
    }

    /// <summary>Progression features of a progressive frame (ITU-T T.81 G.1.1.1): refinement scans, spectral selection, DC
    /// scans that do not interleave every component, and partial progressions (coefficients never coded or not refined to
    /// bit 0).</summary>
    public static List<string> ProgressiveFeatures(JpegInfo info)
    {
        var features = new List<string>();
        var ids = info.Components.Select(c => c.Id).ToList();
        var low = new Dictionary<(int, int), int?>();
        foreach (var id in ids)
        {
            for (var k = 0; k < 64; k++)
                low[(id, k)] = null;
        }

        foreach (var scan in info.ScanParameters)
        {
            var (components, start, end, high, al) = (scan.Components, scan.Start, scan.End, scan.High, scan.Low);
            if (high != 0 && start == 0)
                features.Add("jpeg.progressive=dc-refinement");
            if (high != 0 && start > 0)
                features.Add("jpeg.progressive=ac-refinement");
            if (start > 0 && (start, end) != (1, 63))
                features.Add("jpeg.progressive=spectral-selection");
            if (start == 0 && high == 0 && components.Count < ids.Count)
                features.Add("jpeg.progressive=dc-separate");
            if (high == 0 && al != 0)
                features.Add("jpeg.progressive=successive-approximation");
            foreach (var id in components)
            {
                for (var k = start; k <= Math.Min(end, 63); k++)
                    low[(id, k)] = al;
            }
        }

        if (low.Values.Any(value => value != 0))
            features.Add("jpeg.progressive=partial");
        return features;
    }

    /// <summary>bytes.decode("ascii", "replace").</summary>
    public static string DecodeAsciiReplace(ReadOnlySpan<byte> data)
    {
        var chars = new char[data.Length];
        for (var i = 0; i < data.Length; i++)
            chars[i] = data[i] < 0x80 ? (char)data[i] : '�';
        return new string(chars);
    }

    /// <summary>APP1 Exif segment with a little-endian TIFF header and a single Orientation (0x0112) entry.</summary>
    public static byte[] ExifOrientationSegment(int orientation)
    {
        var tiff = new ByteBuilder().Ascii("II*\0").U32LE(8).U16LE(1).U16LE(0x0112).U16LE(3).U32LE(1).U16LE(orientation).U16LE(0).U32LE(0).ToArray();
        var body = Bytes.Concat(Bytes.Ascii("Exif\0\0"), tiff);
        return new ByteBuilder().U8(0xFF).U8(0xE1).U16BE(body.Length + 2).Bytes(body).ToArray();
    }
}
