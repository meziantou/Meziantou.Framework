namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The coding parameters that one header declares: the main header for the whole image, or the header of the first
/// tile-part of a tile for that tile (T.800, A.3: a marker segment of a tile-part header overrides the main header).
/// </summary>
internal sealed class Jpeg2000HeaderSet
{
    private readonly int _componentCount;
    private Jpeg2000CodingStyle?[]? _componentStyles;
    private Jpeg2000Quantization?[]? _componentQuantizations;
    private int[]? _regionShifts;

    public Jpeg2000HeaderSet(int componentCount)
    {
        _componentCount = componentCount;
    }

    /// <summary>Gets or sets the COD marker segment.</summary>
    public Jpeg2000CodingDefaults? CodingDefaults { get; set; }

    /// <summary>Gets or sets the QCD marker segment.</summary>
    public Jpeg2000Quantization? Quantization { get; set; }

    /// <summary>Gets the progressions of the POC marker segments, or <see langword="null"/> when there is none.</summary>
    public List<Jpeg2000ProgressionChange>? Progressions { get; private set; }

    /// <summary>Gets the list the POC marker segments are appended to.</summary>
    public List<Jpeg2000ProgressionChange> GetOrCreateProgressions() => Progressions ??= [];

    /// <summary>Gets the COC marker segment of a component, or <see langword="null"/>.</summary>
    public Jpeg2000CodingStyle? GetComponentStyle(int component) => _componentStyles?[component];

    /// <summary>Records the COC marker segment of a component.</summary>
    /// <returns><see langword="false"/> if the header already has one for the component.</returns>
    public bool TrySetComponentStyle(int component, Jpeg2000CodingStyle style)
    {
        _componentStyles ??= new Jpeg2000CodingStyle?[_componentCount];
        if (_componentStyles[component] is not null)
            return false;

        _componentStyles[component] = style;
        return true;
    }

    /// <summary>Gets the QCC marker segment of a component, or <see langword="null"/>.</summary>
    public Jpeg2000Quantization? GetComponentQuantization(int component) => _componentQuantizations?[component];

    /// <summary>Records the QCC marker segment of a component.</summary>
    /// <returns><see langword="false"/> if the header already has one for the component.</returns>
    public bool TrySetComponentQuantization(int component, Jpeg2000Quantization quantization)
    {
        _componentQuantizations ??= new Jpeg2000Quantization?[_componentCount];
        if (_componentQuantizations[component] is not null)
            return false;

        _componentQuantizations[component] = quantization;
        return true;
    }

    /// <summary>Gets the shift of the region of interest of a component (RGN marker segment), or -1 when the header has none.</summary>
    public int GetRegionShift(int component) => _regionShifts is null ? -1 : _regionShifts[component];

    /// <summary>Records the RGN marker segment of a component.</summary>
    /// <returns><see langword="false"/> if the header already has one for the component.</returns>
    public bool TrySetRegionShift(int component, int shift)
    {
        if (_regionShifts is null)
        {
            _regionShifts = new int[_componentCount];
            _regionShifts.AsSpan().Fill(-1);
        }

        if (_regionShifts[component] >= 0)
            return false;

        _regionShifts[component] = shift;
        return true;
    }
}
