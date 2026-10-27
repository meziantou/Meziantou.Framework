using System.Runtime.CompilerServices;
using Meziantou.Framework.Imaging.Typography;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Loads the faces of the installed fonts (<see cref="SystemFontDescriptor"/>): a file is read once while one of its
/// fonts is in use, its faces share its bytes, and a face loaded again with the same options is the same font.
/// </summary>
/// <remarks>
/// The cache holds the files weakly: each font of a file keeps its file alive, so a file whose fonts are all
/// unreachable is collected, and read again when one of its faces is loaded again.
/// </remarks>
internal static class InstalledFontFiles
{
    // CSS Fonts Module Level 4, §2.4 "Font style": an oblique style without an angle is 14 degrees. The slnt axis
    // counts the angle counter-clockwise, so a text that leans to the right is negative.
    private const double DefaultObliqueAngle = -14;

    private static readonly Lock Lock = new();
    private static readonly Dictionary<FileKey, WeakReference<FileEntry>> Files = [];
    private static int s_pruneThreshold = 64;

    public static Font Load(SystemFontDescriptor face, FontLoadOptions options)
    {
        var key = new FileKey(face.FilePath, options);
        lock (Lock)
        {
            if (Files.TryGetValue(key, out var reference) && reference.TryGetTarget(out var cached))
                return cached.GetFont(face.FaceIndex);
        }

        // The file is read outside the lock: two threads may read it, and the first one is kept.
        var faces = FontFiles.Load(face.FilePath, options);
        FileEntry entry;
        lock (Lock)
        {
            if (Files.TryGetValue(key, out var reference) && reference.TryGetTarget(out var cached))
            {
                entry = cached;
            }
            else
            {
                entry = new FileEntry(faces);
                Files[key] = new WeakReference<FileEntry>(entry);
                Prune();
            }
        }

        return entry.GetFont(face.FaceIndex);
    }

    /// <summary>
    /// Gives a variable font the style that was asked for: the weight on its <c>wght</c> axis, the italic on its
    /// <c>ital</c> axis, or, without one, an oblique angle on its <c>slnt</c> axis. The values are brought into the range
    /// of the axes; a font without these axes is returned as it is.
    /// </summary>
    public static Font ApplyStyle(Font font, int weight, bool isItalic)
    {
        var axes = font.Face.Axes;
        if (axes.IsEmpty)
            return font;

        var settings = new List<FontVariationSetting>(3);
        var hasItalicAxis = false;
        foreach (var axis in axes)
        {
            if (axis.Tag == FontTags.WeightAxis)
            {
                settings.Add(new FontVariationSetting("wght", weight));
            }
            else if (axis.Tag == FontTags.ItalicAxis)
            {
                hasItalicAxis = true;
                settings.Add(new FontVariationSetting("ital", isItalic ? 1 : 0));
            }
        }

        if (!hasItalicAxis)
        {
            foreach (var axis in axes)
            {
                // A face that is italic by itself is not slanted again.
                if (axis.Tag == FontTags.SlantAxis && !(isItalic && font.IsItalic))
                {
                    settings.Add(new FontVariationSetting("slnt", isItalic ? DefaultObliqueAngle : 0));
                }
            }
        }

        return settings.Count == 0 ? font : font.WithVariations([.. settings]);
    }

    /// <summary>Removes the files whose fonts were all collected, when the number of files doubled since the last time.</summary>
    private static void Prune()
    {
        if (Files.Count < s_pruneThreshold)
            return;

        foreach (var (key, reference) in Files)
        {
            if (!reference.TryGetTarget(out _))
            {
                Files.Remove(key);
            }
        }

        s_pruneThreshold = Math.Max(64, Files.Count * 2);
    }

    private readonly record struct FileKey(string Path, FontLoadOptions Options)
    {
        // The paths are the ones of the descriptors, which are full paths; the options are immutable, compared by reference.
        public bool Equals(FileKey other) => string.Equals(Path, other.Path, StringComparison.Ordinal) && ReferenceEquals(Options, other.Options);

        public override int GetHashCode() => HashCode.Combine(StringComparer.Ordinal.GetHashCode(Path), RuntimeHelpers.GetHashCode(Options));
    }

    /// <summary>The faces of a file, and the fonts made of them: each font keeps the entry alive.</summary>
    private sealed class FileEntry(FontFace[] faces)
    {
        private readonly Font?[] _fonts = new Font?[faces.Length];

        public Font GetFont(int index)
        {
            // The file may have changed since the fonts were scanned.
            if ((uint)index >= (uint)faces.Length)
                throw new InvalidImageContentException($"The font file has {faces.Length} faces; it no longer has the face {index}.", ImageFormat.Unknown);

            lock (_fonts)
            {
                return _fonts[index] ??= new Font(faces[index], owner: this);
            }
        }
    }
}
