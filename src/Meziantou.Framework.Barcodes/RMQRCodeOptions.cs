namespace Meziantou.Framework.Barcodes;

/// <summary>
/// Options for the creation of rectangular Micro QR codes by <see cref="QRCode.CreateRMQR(string, ErrorCorrectionLevel, RMQRCodeOptions)"/>.
/// </summary>
/// <remarks>
/// <para>
/// An rMQR symbol is 7, 9, 11, 13, 15 or 17 modules high, and 27, 43, 59, 77, 99 or 139 modules wide (the widths of 27
/// modules only exist with a height of 11 or 13). Among the sizes that satisfy these options and hold the data, the
/// symbol has the one with the fewest modules.
/// </para>
/// <para>
/// The sizes are numbers of modules, not the size of the image: the <c>ModuleSize</c> of the renderers sets how many
/// pixels or units a module takes.
/// </para>
/// </remarks>
public sealed class RMQRCodeOptions
{
    /// <summary>
    /// Gets or sets the exact height of the symbol in modules (7, 9, 11, 13, 15 or 17), or <see langword="null"/> for any height.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a height of rMQR symbols.</exception>
    public int? Height
    {
        get;
        set
        {
            if (value is not (null or 7 or 9 or 11 or 13 or 15 or 17))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The height of an rMQR symbol is 7, 9, 11, 13, 15 or 17 modules.");

            field = value;
        }
    }

    /// <summary>
    /// Gets or sets the exact width of the symbol in modules (27, 43, 59, 77, 99 or 139), or <see langword="null"/> for any width.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a width of rMQR symbols.</exception>
    public int? Width
    {
        get;
        set
        {
            if (value is not (null or 27 or 43 or 59 or 77 or 99 or 139))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The width of an rMQR symbol is 27, 43, 59, 77, 99 or 139 modules.");

            field = value;
        }
    }

    /// <summary>
    /// Gets or sets the largest height of the symbol in modules, or <see langword="null"/> for no limit.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than 7, the smallest height of rMQR symbols.</exception>
    public int? MaximumHeight
    {
        get;
        set
        {
            if (value is < 7)
                throw new ArgumentOutOfRangeException(nameof(value), value, "The smallest height of an rMQR symbol is 7 modules.");

            field = value;
        }
    }

    /// <summary>
    /// Gets or sets the largest width of the symbol in modules, or <see langword="null"/> for no limit.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than 27, the smallest width of rMQR symbols.</exception>
    public int? MaximumWidth
    {
        get;
        set
        {
            if (value is < 27)
                throw new ArgumentOutOfRangeException(nameof(value), value, "The smallest width of an rMQR symbol is 27 modules.");

            field = value;
        }
    }
}
