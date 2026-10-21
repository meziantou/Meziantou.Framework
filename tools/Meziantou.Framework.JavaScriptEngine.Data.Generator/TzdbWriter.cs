using System.Buffers;
namespace Meziantou.Framework.JavaScriptEngine.Data.Generator;

/// <summary>
/// Writes the uncompressed content of tzdb.bin, read by TimeZoneDatabase in the engine. Integers are LEB128 varints
/// (signed ones zigzag-encoded), strings are ASCII prefixed by their length.
/// <code>
/// "MFTZ" formatVersion(1) version abbreviationCount abbreviation*
/// zoneCount (name dataLength data)*   (sorted by name)
/// linkCount (name targetZoneIndex)*   (sorted by name)
/// windowsCount (windowsId ianaId)*
/// zone data: typeCount (offset isDst abbreviationIndex)* initialType transitionCount (timeDelta typeIndex:byte)*
///            hasRule [firstYear standardOffset ruleCount (month dayKind day weekday at atKind save typeIndex)*]
/// </code>
/// </summary>
internal static class TzdbWriter
{
    public static byte[] Write(string version, IReadOnlyList<CompiledZone> zones, IReadOnlyDictionary<string, string> links, IReadOnlyList<(string WindowsId, string IanaId)> windowsIds)
    {
        var sortedZones = zones.OrderBy(zone => zone.Name, StringComparer.Ordinal).ToList();
        var zoneIndexes = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < sortedZones.Count; i++)
        {
            zoneIndexes.Add(sortedZones[i].Name, i);
        }

        var abbreviations = sortedZones.SelectMany(zone => zone.Types).Select(type => type.Abbreviation).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        var abbreviationIndexes = abbreviations.Select((value, index) => (value, index)).ToDictionary(item => item.value, item => item.index, StringComparer.Ordinal);

        var output = new Buffer();
        output.WriteBytes("MFTZ"u8);
        output.WriteByte(1);
        output.WriteString(version);
        output.WriteVarUInt((ulong)abbreviations.Count);
        foreach (var abbreviation in abbreviations)
        {
            output.WriteString(abbreviation);
        }

        output.WriteVarUInt((ulong)sortedZones.Count);
        foreach (var zone in sortedZones)
        {
            var data = WriteZone(zone, abbreviationIndexes);
            output.WriteString(zone.Name);
            output.WriteVarUInt((ulong)data.Length);
            output.WriteBytes(data);
        }

        var sortedLinks = links.OrderBy(link => link.Key, StringComparer.Ordinal).ToList();
        output.WriteVarUInt((ulong)sortedLinks.Count);
        foreach (var (name, target) in sortedLinks)
        {
            output.WriteString(name);
            output.WriteVarUInt((ulong)zoneIndexes[ResolveLink(target, links)]);
        }

        output.WriteVarUInt((ulong)windowsIds.Count);
        foreach (var (windowsId, ianaId) in windowsIds)
        {
            output.WriteString(windowsId);
            output.WriteString(ianaId);
        }

        return output.ToArray();
    }

    /// <summary>Follows a chain of links to a zone name.</summary>
    public static string ResolveLink(string name, IReadOnlyDictionary<string, string> links)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (links.TryGetValue(name, out var target))
        {
            if (!visited.Add(name))
                throw new InvalidDataException("Circular link: " + name);

            name = target;
        }

        return name;
    }

    private static byte[] WriteZone(CompiledZone zone, Dictionary<string, int> abbreviationIndexes)
    {
        if (zone.Types.Count > byte.MaxValue)
            throw new InvalidDataException($"{zone.Name}: too many local time types");

        var output = new Buffer();
        output.WriteVarUInt((ulong)zone.Types.Count);
        foreach (var type in zone.Types)
        {
            output.WriteVarInt(type.OffsetSeconds);
            output.WriteByte(type.IsDst ? (byte)1 : (byte)0);
            output.WriteVarUInt((ulong)abbreviationIndexes[type.Abbreviation]);
        }

        output.WriteVarUInt((ulong)zone.InitialType);
        output.WriteVarUInt((ulong)zone.Transitions.Count);
        var previous = 0L;
        foreach (var (time, type) in zone.Transitions)
        {
            output.WriteVarInt(time - previous);
            output.WriteByte((byte)type);
            previous = time;
        }

        if (zone.RecurringRules.Count == 0)
        {
            output.WriteByte(0);
        }
        else
        {
            output.WriteByte(1);
            output.WriteVarInt(zone.LastExplicitYear + 1);
            output.WriteVarInt(zone.StandardOffsetSeconds);
            output.WriteVarUInt((ulong)zone.RecurringRules.Count);
            foreach (var (rule, typeIndex) in zone.RecurringRules)
            {
                output.WriteByte((byte)rule.Month);
                output.WriteByte((byte)rule.Day.Kind);
                output.WriteByte((byte)rule.Day.Day);
                output.WriteByte((byte)rule.Day.Weekday);
                output.WriteVarInt(rule.AtSeconds);
                output.WriteByte((byte)rule.AtKind);
                output.WriteVarInt(rule.SaveSeconds);
                output.WriteVarUInt((ulong)typeIndex);
            }
        }

        return output.ToArray();
    }

    private sealed class Buffer
    {
        private readonly ArrayBufferWriter<byte> _buffer = new();

        public void WriteByte(byte value) => _buffer.Write([value]);

        public void WriteBytes(ReadOnlySpan<byte> value) => _buffer.Write(value);

        public void WriteVarUInt(ulong value)
        {
            while (value >= 0x80)
            {
                WriteByte((byte)(value | 0x80));
                value >>= 7;
            }

            WriteByte((byte)value);
        }

        public void WriteVarInt(long value) => WriteVarUInt((ulong)((value << 1) ^ (value >> 63)));

        public void WriteString(string value)
        {
            foreach (var c in value)
            {
                if (c > 0x7F)
                    throw new InvalidDataException("Non-ASCII string: " + value);
            }

            WriteVarUInt((ulong)value.Length);
            WriteBytes(Encoding.ASCII.GetBytes(value));
        }

        public byte[] ToArray() => _buffer.WrittenSpan.ToArray();
    }
}
