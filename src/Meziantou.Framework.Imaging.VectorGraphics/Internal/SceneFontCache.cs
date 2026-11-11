using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Meziantou.Framework.Imaging.Typography;
using Meziantou.Framework.VectorGraphics;

namespace Meziantou.Framework.Imaging.VectorGraphics.Internal;

/// <summary>The fonts of <c>Meziantou.Framework.Imaging</c> that draw the faces of a <see cref="SceneFontCollection"/>, loaded once per face.</summary>
internal static class SceneFontCache
{
    private static readonly ConditionalWeakTable<SceneFontFace, Font> Fonts = [];
    private static readonly ConditionalWeakTable<SceneFontCollection, ConcurrentDictionary<(string? Family, int Weight, bool Italic), FontStack>> Stacks = [];

    /// <summary>Gets the fonts that draw a text: the font of its family and style, then the fonts for the characters it does not have.</summary>
    public static FontStack GetStack(SceneFontCollection fonts, SceneFont font, string? defaultFamily)
    {
        var stacks = Stacks.GetValue(fonts, static _ => new());
        return stacks.GetOrAdd((font.Family ?? defaultFamily, font.Weight, font.Italic), static (key, fonts) =>
        {
            var faces = fonts.GetFaces(new SceneFont(1, key.Weight, key.Italic, key.Family));
            var loaded = new Font[faces.Count];
            for (var i = 0; i < loaded.Length; i++)
            {
                loaded[i] = GetFont(faces[i]);
            }

            return new FontStack(loaded[0], loaded.AsSpan(1));
        }, fonts);
    }

    private static Font GetFont(SceneFontFace face) => Fonts.GetValue(face, static face => FontCollection.Load(face.Data.Span)[face.Index]);
}
