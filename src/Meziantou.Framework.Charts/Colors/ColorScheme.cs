namespace Meziantou.Framework.Charts;

/// <summary>A continuous color ramp, interpolated in the OKLab color space between evenly spaced stops.</summary>
public sealed class ColorScheme
{
    /// <summary>Initializes a new instance of the <see cref="ColorScheme"/> class.</summary>
    /// <param name="name">The name of the scheme.</param>
    /// <param name="stops">The colors of the ramp, from its start to its end. At least two.</param>
    /// <exception cref="ArgumentException"><paramref name="stops"/> has fewer than two colors.</exception>
    public ColorScheme(string name, IEnumerable<ChartColor> stops)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(stops);
        Name = name;
        Stops = [.. stops];
        if (Stops.Count < 2)
            throw new ArgumentException("A color scheme needs at least two colors.", nameof(stops));
    }

    /// <summary>Gets the name of the scheme.</summary>
    public string Name { get; }

    /// <summary>Gets the colors of the ramp.</summary>
    public IReadOnlyList<ChartColor> Stops { get; }

    /// <summary>Gets a perceptually uniform ramp from dark purple to yellow (viridis, CC0).</summary>
    public static ColorScheme Viridis { get; } = Create("Viridis", "#440154", "#482878", "#3e4989", "#31688e", "#26828e", "#1f9e89", "#35b779", "#6ece58", "#b5de2b", "#fde725");

    /// <summary>Gets a perceptually uniform ramp from black to light yellow (magma, CC0).</summary>
    public static ColorScheme Magma { get; } = Create("Magma", "#000004", "#1c1044", "#4f127b", "#812581", "#b5367a", "#e55964", "#fb8761", "#fec287", "#fcfdbf");

    /// <summary>Gets a ramp from a very light blue to a dark blue.</summary>
    public static ColorScheme Blues { get; } = Create("Blues", "#f2f7fd", "#a9cbee", "#4a8fd6", "#1d5ba6", "#0b2f66");

    /// <summary>Gets a ramp from a very light green to a dark green.</summary>
    public static ColorScheme Greens { get; } = Create("Greens", "#f3faf1", "#b0dfa5", "#5bb664", "#22823d", "#064a1d");

    /// <summary>Gets a ramp from a very light orange to a dark orange.</summary>
    public static ColorScheme Oranges { get; } = Create("Oranges", "#fff6eb", "#fdc58d", "#f8853a", "#cf4f0c", "#7c2a04");

    /// <summary>Gets a ramp from a very light red to a dark red.</summary>
    public static ColorScheme Reds { get; } = Create("Reds", "#fff3ef", "#fcb19b", "#f45f48", "#be1d22", "#6a0611");

    /// <summary>Gets a ramp from white to almost black.</summary>
    public static ColorScheme Greys { get; } = Create("Greys", "#ffffff", "#c8c8c8", "#8a8a8a", "#4d4d4d", "#111111");

    /// <summary>Gets a diverging ramp from dark red to dark blue, through a light gray.</summary>
    public static ColorScheme RedBlue { get; } = Create("RedBlue", "#6b0a22", "#c4413f", "#f3a888", "#f5f2f0", "#94c5df", "#3a7db8", "#0a305e");

    /// <summary>Gets a diverging ramp from dark red to dark green, through a light yellow.</summary>
    public static ColorScheme RedYellowGreen { get; } = Create("RedYellowGreen", "#a3102b", "#ea6a45", "#fbd28a", "#f7f6c3", "#b9e08a", "#57b55f", "#0b6a39");

    /// <summary>Returns the color at a position of the ramp.</summary>
    /// <param name="t">The position, from 0 (the first stop) to 1 (the last stop). It is clamped.</param>
    /// <returns>The color.</returns>
    public ChartColor Interpolate(double t)
    {
        if (double.IsNaN(t))
            return Stops[0];

        t = Math.Clamp(t, 0, 1);
        var segment = t * (Stops.Count - 1);
        var index = Math.Min((int)Math.Floor(segment), Stops.Count - 2);
        return OkLab.Interpolate(Stops[index], Stops[index + 1], segment - index);
    }

    /// <inheritdoc/>
    public override string ToString() => Name;

    private static ColorScheme Create(string name, params string[] colors) => new(name, colors.Select(ChartColor.Parse));
}
