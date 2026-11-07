using System.Runtime.CompilerServices;
using Meziantou.Framework.WebPlatform.Css.Fonts;
using Meziantou.Framework.WebPlatform.Rendering;
using ImagingFont = Meziantou.Framework.Imaging.Typography.Font;
using SharedFontException = Meziantou.Framework.Fonts.FontException;

namespace Meziantou.Framework.WebPlatform.Imaging;

/// <summary>
/// The fonts of the font files of the display lists, per backend: each face of a font file, at each instance of its variation
/// axes, is a font of Meziantou.Framework.Imaging made from the face the layout measured the glyphs with (the font reader is
/// shared by the two libraries, so a font file is read once). The entries live as long as their font file.
/// </summary>
internal sealed class FontCache
{
    private readonly ConditionalWeakTable<FontFile, FontFileFaces> _files = [];

    /// <summary>Returns the font of a glyph run, or null when the font file has no such face.</summary>
    public ImagingFont? Get(DisplayFont font) => _files.GetValue(font.File, static file => new FontFileFaces(file)).Get(font.FaceIndex, font.Variations);

    private sealed class FontFileFaces(FontFile file)
    {
        private readonly Lock _lock = new();
        private readonly Dictionary<InstanceKey, ImagingFont?> _faces = [];

        public ImagingFont? Get(int faceIndex, FontVariationSetting[] variations)
        {
            lock (_lock)
            {
                var key = new InstanceKey(faceIndex, variations);
                if (!_faces.TryGetValue(key, out var face))
                {
                    face = CreateFont(faceIndex, variations);
                    _faces.Add(key, face);
                }

                return face;
            }
        }

        /// <summary>Returns the font of Meziantou.Framework.Imaging of a face at an instance, or null when the file has no such face.</summary>
        private ImagingFont? CreateFont(int faceIndex, FontVariationSetting[] variations)
        {
            if ((uint)faceIndex >= (uint)file.Faces.Count)
                return null;

            var font = file.Faces[faceIndex];
            var face = variations.Length == 0 || !font.Variations.IsVariable ? font.Face : font.Variations.CreateInstance(variations).Face;
            try
            {
                return new ImagingFont(face);
            }
            catch (SharedFontException)
            {
                // The named instances of the face are malformed: its glyphs are not drawn
                return null;
            }
        }
    }

    /// <summary>A face of a font file with the values of its variation axes (compared by value).</summary>
    private readonly struct InstanceKey(int faceIndex, FontVariationSetting[] variations) : IEquatable<InstanceKey>
    {
        private readonly int _faceIndex = faceIndex;
        private readonly FontVariationSetting[] _variations = variations;

        public bool Equals(InstanceKey other) => _faceIndex == other._faceIndex && _variations.AsSpan().SequenceEqual(other._variations);

        public override bool Equals([NotNullWhen(true)] object? obj) => obj is InstanceKey other && Equals(other);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(_faceIndex);
            foreach (var variation in _variations)
            {
                hash.Add(variation);
            }

            return hash.ToHashCode();
        }
    }
}
