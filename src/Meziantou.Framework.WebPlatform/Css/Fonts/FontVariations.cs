using System.Runtime.InteropServices;
using SharedFontFace = Meziantou.Framework.Fonts.FontFace;
using SharedFontException = Meziantou.Framework.Fonts.FontException;

namespace Meziantou.Framework.WebPlatform.Css.Fonts;

/// <summary>A variation axis of a variable font (https://learn.microsoft.com/typography/opentype/spec/fvar#variationaxisrecord).</summary>
/// <param name="Tag">The tag of the axis (e.g. <c>wght</c>).</param>
/// <param name="MinValue">The minimum value of the axis, in user coordinates.</param>
/// <param name="DefaultValue">The default value of the axis.</param>
/// <param name="MaxValue">The maximum value of the axis.</param>
/// <param name="IsHidden">Whether the axis should not be exposed to the users (<c>HIDDEN_AXIS</c> flag).</param>
/// <param name="NameId">The name identifier of the name of the axis.</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct FontVariationAxis(uint Tag, double MinValue, double DefaultValue, double MaxValue, bool IsHidden, ushort NameId);

/// <summary>A named instance of a variable font (https://learn.microsoft.com/typography/opentype/spec/fvar#instancerecord).</summary>
/// <param name="SubfamilyNameId">The name identifier of the subfamily name of the instance (e.g. "Bold").</param>
/// <param name="Coordinates">The user coordinates of the instance, one per axis.</param>
internal sealed record FontNamedInstance(ushort SubfamilyNameId, double[] Coordinates);

/// <summary>A value of a variation axis, in user coordinates (e.g. <c>font-variation-settings: "wght" 650</c>).</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct FontVariationSetting(uint Tag, double Value);

/// <summary>
/// An instance of a variable font: the face of the font reader at the values of its axes, whose advances, metrics and
/// layout deltas are those of the instance. The instance of a font is created by <see cref="FontVariations.CreateInstance"/>.
/// </summary>
internal sealed class FontVariationInstance
{
    internal FontVariationInstance(SharedFontFace face)
    {
        Face = face;
        if (face.Instance is { } instance)
        {
            // The normalized coordinates are 2.14 numbers (https://learn.microsoft.com/typography/opentype/spec/otvaroverview#coordinate-scales-and-normalization)
            var coordinates = new double[instance.Coordinates.Length];
            for (var i = 0; i < coordinates.Length; i++)
            {
                coordinates[i] = instance.Coordinates[i] / 16384.0;
            }

            NormalizedCoordinates = coordinates;
        }
        else
        {
            NormalizedCoordinates = [];
        }

        IsDefault = face.Instance is null;
    }

    /// <summary>The face of the instance.</summary>
    internal SharedFontFace Face { get; }

    /// <summary>The normalized coordinates, from -1 to 1, after the avar mapping; empty for the default instance.</summary>
    public IReadOnlyList<double> NormalizedCoordinates { get; }

    /// <summary>Whether the instance is the default instance of the font (all the normalized coordinates are 0).</summary>
    public bool IsDefault { get; }

    internal double GetCoordinate(int axis) => axis < NormalizedCoordinates.Count ? NormalizedCoordinates[axis] : 0;
}

/// <summary>
/// The variation axes and the named instances of a font (https://learn.microsoft.com/typography/opentype/spec/otvaroverview),
/// read by the font reader, which also applies the avar, HVAR, MVAR and gvar tables to the instances.
/// </summary>
internal sealed class FontVariations
{
    private readonly SharedFontFace _face;
    private readonly FontVariationAxis[] _axes;
    private readonly FontNamedInstance[] _instances;

    internal FontVariations(SharedFontFace face)
    {
        _face = face;
        var axes = face.Axes;
        _axes = new FontVariationAxis[axes.Length];
        for (var i = 0; i < _axes.Length; i++)
        {
            var axis = axes[i];
            _axes[i] = new FontVariationAxis(axis.Tag, axis.Minimum / 65536.0, axis.Default / 65536.0, axis.Maximum / 65536.0, axis.IsHidden, (ushort)axis.NameId);
        }

        var instances = face.NamedInstances;
        _instances = new FontNamedInstance[instances.Length];
        for (var i = 0; i < _instances.Length; i++)
        {
            var coordinates = new double[instances[i].Coordinates.Length];
            for (var axis = 0; axis < coordinates.Length; axis++)
            {
                coordinates[axis] = instances[i].Coordinates[axis] / 65536.0;
            }

            _instances[i] = new FontNamedInstance((ushort)instances[i].NameId, coordinates);
        }
    }

    /// <summary>Whether the font is a variable font (it has variation axes).</summary>
    public bool IsVariable => _axes.Length > 0;

    /// <summary>The variation axes of the font, in the order of the fvar table.</summary>
    public IReadOnlyList<FontVariationAxis> Axes => _axes;

    /// <summary>The named instances of the font.</summary>
    public IReadOnlyList<FontNamedInstance> NamedInstances => _instances;

    /// <summary>Returns the axis with the tag, or null when the font does not have it.</summary>
    public FontVariationAxis? GetAxis(uint tag)
    {
        foreach (var axis in _axes)
        {
            if (axis.Tag == tag)
                return axis;
        }

        return null;
    }

    /// <summary>
    /// Creates the instance of the font for the values of the axes (the axes without value have their default value, and
    /// the last value of an axis wins). The values are clamped to the range of their axis, normalized and mapped by the
    /// avar table (https://learn.microsoft.com/typography/opentype/spec/otvaroverview#coordinate-scales-and-normalization).
    /// A font whose variation tables are malformed has its default instance.
    /// </summary>
    public FontVariationInstance CreateInstance(ReadOnlySpan<FontVariationSetting> settings)
    {
        var axes = _face.Axes;
        var coordinates = new int[axes.Length];
        for (var i = 0; i < coordinates.Length; i++)
        {
            coordinates[i] = axes[i].Default;
            foreach (var setting in settings)
            {
                if (setting.Tag == axes[i].Tag && !double.IsNaN(setting.Value))
                {
                    // A 16.16 fixed-point number; the face brings it into the range of the axis
                    coordinates[i] = (int)Math.Clamp(Math.Round(setting.Value * 65536, MidpointRounding.AwayFromZero), int.MinValue, int.MaxValue);
                }
            }
        }

        try
        {
            return new FontVariationInstance(_face.WithCoordinates(coordinates));
        }
        catch (SharedFontException)
        {
            return new FontVariationInstance(_face);
        }
    }
}
