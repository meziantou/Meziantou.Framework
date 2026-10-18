using System.Runtime.CompilerServices;
using Meziantou.Framework.WebPlatform.Css.Fonts;
using Meziantou.Framework.WebPlatform.Rendering;
using ImagingFont = Meziantou.Framework.Imaging.Typography.Font;
using ImagingFontCollection = Meziantou.Framework.Imaging.Typography.FontCollection;
using ImagingVariationSetting = Meziantou.Framework.Imaging.Typography.FontVariationSetting;

namespace Meziantou.Framework.WebPlatform.Imaging;

/// <summary>
/// The fonts of the font files of the display lists, per backend: each face of a font file, at each instance of its variation
/// axes, is a font of Meziantou.Framework.Imaging, which draws the glyphs by id (the layout measured them with the managed font
/// reader of the library, in the same font file). The entries live as long as their font file.
/// </summary>
internal sealed class FontCache
{
    private readonly ConditionalWeakTable<FontFile, FontFileFaces> _files = [];

    /// <summary>Returns the font of a glyph run, or null when the font file has no such face or Meziantou.Framework.Imaging cannot read it.</summary>
    public ImagingFont? Get(DisplayFont font) => _files.GetValue(font.File, static file => new FontFileFaces(file)).Get(font.FaceIndex, font.Variations);

    private sealed class FontFileFaces(FontFile file)
    {
        private readonly Lock _lock = new();
        private readonly Dictionary<InstanceKey, ImagingFont?> _faces = [];
        private ImagingFontCollection? _collection;
        private bool _collectionLoaded;

        public ImagingFont? Get(int faceIndex, FontVariationSetting[] variations)
        {
            lock (_lock)
            {
                var key = new InstanceKey(faceIndex, variations);
                if (!_faces.TryGetValue(key, out var face))
                {
                    face = GetImagingFont(faceIndex, variations);
                    _faces.Add(key, face);
                }

                return face;
            }
        }

        /// <summary>Returns the font of Meziantou.Framework.Imaging of a face at an instance, or null when it cannot read the face.</summary>
        private ImagingFont? GetImagingFont(int faceIndex, FontVariationSetting[] variations)
        {
            if (!_collectionLoaded)
            {
                _collectionLoaded = true;
                try
                {
                    _collection = ImagingFontCollection.Load(file.Data);
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    // The managed font reader of the library accepts a font that Meziantou.Framework.Imaging rejects: its glyphs
                    // are not drawn
                    _collection = null;
                }
            }

            if (_collection is not { } collection || (uint)faceIndex >= (uint)collection.Count)
                return null;

            var font = collection[faceIndex];
            if (variations.Length == 0 || font.VariationAxes.Count == 0)
                return font;

            var settings = new ImagingVariationSetting[variations.Length];
            for (var i = 0; i < settings.Length; i++)
            {
                settings[i] = new ImagingVariationSetting(ToTagString(variations[i].Tag), variations[i].Value);
            }

            try
            {
                return font.WithVariations(settings);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                return null;
            }
        }
    }

    /// <summary>The four characters of an OpenType tag.</summary>
    private static string ToTagString(uint tag) => string.Create(4, tag, static (span, tag) =>
    {
        for (var i = 0; i < 4; i++)
        {
            span[i] = (char)((tag >> (24 - (i * 8))) & 0xFF);
        }
    });

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
