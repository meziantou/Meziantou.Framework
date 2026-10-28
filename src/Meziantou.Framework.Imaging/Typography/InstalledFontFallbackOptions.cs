namespace Meziantou.Framework.Imaging.Typography;

/// <summary>Options of the fallback of a <see cref="FontStack"/> to the installed fonts. Instances are immutable and can be shared.</summary>
/// <seealso cref="FontStack.WithInstalledFallback(SystemFontCollection?, InstalledFontFallbackOptions?)"/>
public sealed class InstalledFontFallbackOptions
{
    /// <summary>Gets the default options.</summary>
    public static InstalledFontFallbackOptions Default { get; } = new();

    /// <summary>Gets the options the installed fonts are loaded with. Defaults to <see cref="FontLoadOptions.Default"/>.</summary>
    /// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
    public FontLoadOptions LoadOptions
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    } = FontLoadOptions.Default;

    /// <summary>
    /// Gets the families that are looked in first, in order, for a character the fonts of the stack do not have, before
    /// the families the platform usually uses for it. Empty by default.
    /// </summary>
    /// <exception cref="ArgumentNullException">The value, or one of its names, is <see langword="null"/>.</exception>
    public IReadOnlyList<string> PreferredFamilies
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            var names = new string[value.Count];
            for (var i = 0; i < names.Length; i++)
            {
                names[i] = value[i] ?? throw new ArgumentNullException(nameof(value), "A family name is null.");
            }

            field = names;
        }
    } = [];
}
