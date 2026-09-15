namespace Meziantou.Framework.Win32;

/// <summary>Represents the zone information stored in a Zone.Identifier alternate data stream.</summary>
/// <remarks>
/// The values are the ones the stream records, not the zone Windows assigns to the file.
/// Use <see cref="MarkOfTheWeb.GetFileZone(string)"/> or <see cref="MarkOfTheWeb.IsUntrusted(string)"/> to make a security decision.
/// </remarks>
public sealed class ZoneIdentifier
{
    private const string SectionName = "ZoneTransfer";

    private ZoneIdentifier(UrlZone zone, string? referrerUrl, string? hostUrl)
    {
        Zone = zone;
        ReferrerUrl = referrerUrl;
        HostUrl = hostUrl;
    }

    /// <summary>Gets the zone recorded by the <c>ZoneId</c> entry, or <see cref="UrlZone.Invalid"/> when there is no usable entry.</summary>
    /// <remarks>The value can be a custom zone, numbered 1000 and above, that has no named <see cref="UrlZone"/> member.</remarks>
    public UrlZone Zone { get; }

    /// <summary>Gets the URL of the page that linked to the file, or <see langword="null"/> when the stream does not record one.</summary>
    [SuppressMessage("Design", "CA1056:URI-like properties should not be strings")]
    public string? ReferrerUrl { get; }

    /// <summary>Gets the URL the file was downloaded from, or <see langword="null"/> when the stream does not record one.</summary>
    [SuppressMessage("Design", "CA1056:URI-like properties should not be strings")]
    public string? HostUrl { get; }

    /// <summary>Parses the content of a Zone.Identifier alternate data stream.</summary>
    /// <param name="content">The content of the stream, as returned by <see cref="MarkOfTheWeb.GetFileZoneContent(string)"/>.</param>
    /// <returns>The zone information found in the <c>[ZoneTransfer]</c> section.</returns>
    /// <remarks>
    /// Section and key names are case-insensitive, and whitespace around keys and values is ignored.
    /// An entry that is repeated with different values is ambiguous, so it is reported as absent.
    /// </remarks>
    public static ZoneIdentifier Parse(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var zoneId = default(Entry);
        var referrerUrl = default(Entry);
        var hostUrl = default(Entry);

        var isInSection = false;
        foreach (var rawLine in content.AsSpan().TrimStart('﻿').EnumerateLines())
        {
            var line = rawLine.Trim();
            if (line.IsEmpty || line[0] is ';')
                continue;

            if (line[0] is '[')
            {
                isInSection = line[^1] is ']' && line[1..^1].Trim().Equals(SectionName, StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!isInSection)
                continue;

            var separatorIndex = line.IndexOf('=');
            if (separatorIndex < 0)
                continue;

            var key = line[..separatorIndex].Trim();
            var value = line[(separatorIndex + 1)..].Trim();
            if (key.Equals("ZoneId", StringComparison.OrdinalIgnoreCase))
            {
                zoneId.Add(value);
            }
            else if (key.Equals("ReferrerUrl", StringComparison.OrdinalIgnoreCase))
            {
                referrerUrl.Add(value);
            }
            else if (key.Equals("HostUrl", StringComparison.OrdinalIgnoreCase))
            {
                hostUrl.Add(value);
            }
        }

        var zone = int.TryParse(zoneId.Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var zoneValue) ? (UrlZone)zoneValue : UrlZone.Invalid;
        return new ZoneIdentifier(zone, referrerUrl.Value, hostUrl.Value);
    }

    private struct Entry
    {
        private string? _value;
        private bool _isAmbiguous;

        /// <summary>Gets the value of the entry, or <see langword="null"/> when it is absent, empty, or ambiguous.</summary>
        public readonly string? Value => string.IsNullOrEmpty(_value) ? null : _value;

        public void Add(ReadOnlySpan<char> value)
        {
            if (_isAmbiguous)
                return;

            if (_value is null)
            {
                _value = value.ToString();
            }
            else if (!value.SequenceEqual(_value))
            {
                _value = null;
                _isAmbiguous = true;
            }
        }
    }
}
