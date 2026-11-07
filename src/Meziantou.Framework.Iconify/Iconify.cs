using Meziantou.Framework.Internals;

namespace Meziantou.Framework;

/// <summary>
/// Provides access to Iconify icons.
/// </summary>
/// <remarks>
/// <para>
/// The icon sets declared with <c>IconifyIconSet</c> items in the project file are generated at build time and exposed as static extension properties
/// of this class, such as <c>Iconify.Mdi.Icons.Home</c>.
/// </para>
/// <para>
/// The <c>Load</c> methods read any icon set in the IconifyJSON format at run time, such as the files of the iconify/icon-sets repository.
/// </para>
/// </remarks>
public static class Iconify
{
    /// <summary>Loads an icon set in the IconifyJSON format from a file.</summary>
    /// <param name="path">The path of the JSON file.</param>
    /// <returns>The icon set, with its aliases resolved.</returns>
    /// <exception cref="FormatException">The file is not a valid icon set.</exception>
    public static IconifyIconSet Load(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        using var stream = File.OpenRead(path);
        return Load(stream);
    }

    /// <summary>Loads an icon set in the IconifyJSON format from a stream.</summary>
    /// <param name="stream">The stream that contains the JSON document, encoded in UTF-8. It is read to the end, but not closed.</param>
    /// <returns>The icon set, with its aliases resolved.</returns>
    /// <exception cref="FormatException">The stream does not contain a valid icon set.</exception>
    public static IconifyIconSet Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return Create(IconifyJsonReader.Read(stream));
    }

    /// <summary>Loads an icon set in the IconifyJSON format from a file.</summary>
    /// <param name="path">The path of the JSON file.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The icon set, with its aliases resolved.</returns>
    /// <exception cref="FormatException">The file is not a valid icon set.</exception>
    public static async Task<IconifyIconSet> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(path);
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, useAsync: true);
        await using (stream.ConfigureAwait(false))
        {
            return await LoadAsync(stream, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Loads an icon set in the IconifyJSON format from a stream.</summary>
    /// <param name="stream">The stream that contains the JSON document, encoded in UTF-8. It is read to the end, but not closed.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The icon set, with its aliases resolved.</returns>
    /// <exception cref="FormatException">The stream does not contain a valid icon set.</exception>
    public static async Task<IconifyIconSet> LoadAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return Create(await IconifyJsonReader.ReadAsync(stream, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>Parses an icon set in the IconifyJSON format.</summary>
    /// <param name="json">The JSON document.</param>
    /// <returns>The icon set, with its aliases resolved.</returns>
    /// <exception cref="FormatException">The text is not a valid icon set.</exception>
    public static IconifyIconSet Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        return Create(IconifyJsonReader.Read(json));
    }

    private static IconifyIconSet Create(IconifyIconSetData data)
    {
        var icons = new IconifyIcon[data.Icons.Count];
        for (var i = 0; i < icons.Length; i++)
        {
            var icon = data.Icons[i];
            icons[i] = new IconifyIcon(data.Prefix, icon.Name, icon.Body, icon.Left, icon.Top, icon.Width, icon.Height, icon.Hidden);
        }

        IconifyIconSetInfo? info = null;
        if (data.Info is { } infoData)
        {
            info = new IconifyIconSetInfo
            {
                Name = infoData.Name,
                AuthorName = infoData.AuthorName,
                AuthorUrl = infoData.AuthorUrl,
                LicenseTitle = infoData.LicenseTitle,
                LicenseSpdx = infoData.LicenseSpdx,
                LicenseUrl = infoData.LicenseUrl,
                Total = infoData.Total,
                Version = infoData.Version,
                Category = infoData.Category,
                Palette = infoData.Palette,
            };
        }

        return new IconifyIconSet(data.Prefix, info, icons);
    }
}
