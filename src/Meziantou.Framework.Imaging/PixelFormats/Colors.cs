namespace Meziantou.Framework.Imaging;

/// <summary>
/// The named colors of CSS, as opaque <see cref="Rgba32"/> values (and <see cref="Transparent"/>). A color converts
/// implicitly to an <see cref="Rgba64"/> and to a brush.
/// </summary>
/// <remarks>
/// The values are sample values: like every color given to the library, they are in the color space of the image they
/// are used with, and no color profile is applied.
/// </remarks>
public static class Colors
{
    /// <summary>Gets transparent black: every component is zero.</summary>
    public static Rgba32 Transparent => new(0, 0, 0, 0);

    /// <summary>Gets the color <c>#F0F8FF</c>.</summary>
    public static Rgba32 AliceBlue => new(0xF0, 0xF8, 0xFF);

    /// <summary>Gets the color <c>#FAEBD7</c>.</summary>
    public static Rgba32 AntiqueWhite => new(0xFA, 0xEB, 0xD7);

    /// <summary>Gets the color <c>#00FFFF</c>.</summary>
    public static Rgba32 Aqua => new(0x00, 0xFF, 0xFF);

    /// <summary>Gets the color <c>#7FFFD4</c>.</summary>
    public static Rgba32 Aquamarine => new(0x7F, 0xFF, 0xD4);

    /// <summary>Gets the color <c>#F0FFFF</c>.</summary>
    public static Rgba32 Azure => new(0xF0, 0xFF, 0xFF);

    /// <summary>Gets the color <c>#F5F5DC</c>.</summary>
    public static Rgba32 Beige => new(0xF5, 0xF5, 0xDC);

    /// <summary>Gets the color <c>#FFE4C4</c>.</summary>
    public static Rgba32 Bisque => new(0xFF, 0xE4, 0xC4);

    /// <summary>Gets the color <c>#000000</c>.</summary>
    public static Rgba32 Black => new(0x00, 0x00, 0x00);

    /// <summary>Gets the color <c>#FFEBCD</c>.</summary>
    public static Rgba32 BlanchedAlmond => new(0xFF, 0xEB, 0xCD);

    /// <summary>Gets the color <c>#0000FF</c>.</summary>
    public static Rgba32 Blue => new(0x00, 0x00, 0xFF);

    /// <summary>Gets the color <c>#8A2BE2</c>.</summary>
    public static Rgba32 BlueViolet => new(0x8A, 0x2B, 0xE2);

    /// <summary>Gets the color <c>#A52A2A</c>.</summary>
    public static Rgba32 Brown => new(0xA5, 0x2A, 0x2A);

    /// <summary>Gets the color <c>#DEB887</c>.</summary>
    public static Rgba32 BurlyWood => new(0xDE, 0xB8, 0x87);

    /// <summary>Gets the color <c>#5F9EA0</c>.</summary>
    public static Rgba32 CadetBlue => new(0x5F, 0x9E, 0xA0);

    /// <summary>Gets the color <c>#7FFF00</c>.</summary>
    public static Rgba32 Chartreuse => new(0x7F, 0xFF, 0x00);

    /// <summary>Gets the color <c>#D2691E</c>.</summary>
    public static Rgba32 Chocolate => new(0xD2, 0x69, 0x1E);

    /// <summary>Gets the color <c>#FF7F50</c>.</summary>
    public static Rgba32 Coral => new(0xFF, 0x7F, 0x50);

    /// <summary>Gets the color <c>#6495ED</c>.</summary>
    public static Rgba32 CornflowerBlue => new(0x64, 0x95, 0xED);

    /// <summary>Gets the color <c>#FFF8DC</c>.</summary>
    public static Rgba32 Cornsilk => new(0xFF, 0xF8, 0xDC);

    /// <summary>Gets the color <c>#DC143C</c>.</summary>
    public static Rgba32 Crimson => new(0xDC, 0x14, 0x3C);

    /// <summary>Gets the color <c>#00FFFF</c>.</summary>
    public static Rgba32 Cyan => new(0x00, 0xFF, 0xFF);

    /// <summary>Gets the color <c>#00008B</c>.</summary>
    public static Rgba32 DarkBlue => new(0x00, 0x00, 0x8B);

    /// <summary>Gets the color <c>#008B8B</c>.</summary>
    public static Rgba32 DarkCyan => new(0x00, 0x8B, 0x8B);

    /// <summary>Gets the color <c>#B8860B</c>.</summary>
    public static Rgba32 DarkGoldenrod => new(0xB8, 0x86, 0x0B);

    /// <summary>Gets the color <c>#A9A9A9</c>.</summary>
    public static Rgba32 DarkGray => new(0xA9, 0xA9, 0xA9);

    /// <summary>Gets the color <c>#006400</c>.</summary>
    public static Rgba32 DarkGreen => new(0x00, 0x64, 0x00);

    /// <summary>Gets the color <c>#BDB76B</c>.</summary>
    public static Rgba32 DarkKhaki => new(0xBD, 0xB7, 0x6B);

    /// <summary>Gets the color <c>#8B008B</c>.</summary>
    public static Rgba32 DarkMagenta => new(0x8B, 0x00, 0x8B);

    /// <summary>Gets the color <c>#556B2F</c>.</summary>
    public static Rgba32 DarkOliveGreen => new(0x55, 0x6B, 0x2F);

    /// <summary>Gets the color <c>#FF8C00</c>.</summary>
    public static Rgba32 DarkOrange => new(0xFF, 0x8C, 0x00);

    /// <summary>Gets the color <c>#9932CC</c>.</summary>
    public static Rgba32 DarkOrchid => new(0x99, 0x32, 0xCC);

    /// <summary>Gets the color <c>#8B0000</c>.</summary>
    public static Rgba32 DarkRed => new(0x8B, 0x00, 0x00);

    /// <summary>Gets the color <c>#E9967A</c>.</summary>
    public static Rgba32 DarkSalmon => new(0xE9, 0x96, 0x7A);

    /// <summary>Gets the color <c>#8FBC8F</c>.</summary>
    public static Rgba32 DarkSeaGreen => new(0x8F, 0xBC, 0x8F);

    /// <summary>Gets the color <c>#483D8B</c>.</summary>
    public static Rgba32 DarkSlateBlue => new(0x48, 0x3D, 0x8B);

    /// <summary>Gets the color <c>#2F4F4F</c>.</summary>
    public static Rgba32 DarkSlateGray => new(0x2F, 0x4F, 0x4F);

    /// <summary>Gets the color <c>#00CED1</c>.</summary>
    public static Rgba32 DarkTurquoise => new(0x00, 0xCE, 0xD1);

    /// <summary>Gets the color <c>#9400D3</c>.</summary>
    public static Rgba32 DarkViolet => new(0x94, 0x00, 0xD3);

    /// <summary>Gets the color <c>#FF1493</c>.</summary>
    public static Rgba32 DeepPink => new(0xFF, 0x14, 0x93);

    /// <summary>Gets the color <c>#00BFFF</c>.</summary>
    public static Rgba32 DeepSkyBlue => new(0x00, 0xBF, 0xFF);

    /// <summary>Gets the color <c>#696969</c>.</summary>
    public static Rgba32 DimGray => new(0x69, 0x69, 0x69);

    /// <summary>Gets the color <c>#1E90FF</c>.</summary>
    public static Rgba32 DodgerBlue => new(0x1E, 0x90, 0xFF);

    /// <summary>Gets the color <c>#B22222</c>.</summary>
    public static Rgba32 Firebrick => new(0xB2, 0x22, 0x22);

    /// <summary>Gets the color <c>#FFFAF0</c>.</summary>
    public static Rgba32 FloralWhite => new(0xFF, 0xFA, 0xF0);

    /// <summary>Gets the color <c>#228B22</c>.</summary>
    public static Rgba32 ForestGreen => new(0x22, 0x8B, 0x22);

    /// <summary>Gets the color <c>#FF00FF</c>.</summary>
    public static Rgba32 Fuchsia => new(0xFF, 0x00, 0xFF);

    /// <summary>Gets the color <c>#DCDCDC</c>.</summary>
    public static Rgba32 Gainsboro => new(0xDC, 0xDC, 0xDC);

    /// <summary>Gets the color <c>#F8F8FF</c>.</summary>
    public static Rgba32 GhostWhite => new(0xF8, 0xF8, 0xFF);

    /// <summary>Gets the color <c>#FFD700</c>.</summary>
    public static Rgba32 Gold => new(0xFF, 0xD7, 0x00);

    /// <summary>Gets the color <c>#DAA520</c>.</summary>
    public static Rgba32 Goldenrod => new(0xDA, 0xA5, 0x20);

    /// <summary>Gets the color <c>#808080</c>.</summary>
    public static Rgba32 Gray => new(0x80, 0x80, 0x80);

    /// <summary>Gets the color <c>#008000</c>.</summary>
    public static Rgba32 Green => new(0x00, 0x80, 0x00);

    /// <summary>Gets the color <c>#ADFF2F</c>.</summary>
    public static Rgba32 GreenYellow => new(0xAD, 0xFF, 0x2F);

    /// <summary>Gets the color <c>#F0FFF0</c>.</summary>
    public static Rgba32 Honeydew => new(0xF0, 0xFF, 0xF0);

    /// <summary>Gets the color <c>#FF69B4</c>.</summary>
    public static Rgba32 HotPink => new(0xFF, 0x69, 0xB4);

    /// <summary>Gets the color <c>#CD5C5C</c>.</summary>
    public static Rgba32 IndianRed => new(0xCD, 0x5C, 0x5C);

    /// <summary>Gets the color <c>#4B0082</c>.</summary>
    public static Rgba32 Indigo => new(0x4B, 0x00, 0x82);

    /// <summary>Gets the color <c>#FFFFF0</c>.</summary>
    public static Rgba32 Ivory => new(0xFF, 0xFF, 0xF0);

    /// <summary>Gets the color <c>#F0E68C</c>.</summary>
    public static Rgba32 Khaki => new(0xF0, 0xE6, 0x8C);

    /// <summary>Gets the color <c>#E6E6FA</c>.</summary>
    public static Rgba32 Lavender => new(0xE6, 0xE6, 0xFA);

    /// <summary>Gets the color <c>#FFF0F5</c>.</summary>
    public static Rgba32 LavenderBlush => new(0xFF, 0xF0, 0xF5);

    /// <summary>Gets the color <c>#7CFC00</c>.</summary>
    public static Rgba32 LawnGreen => new(0x7C, 0xFC, 0x00);

    /// <summary>Gets the color <c>#FFFACD</c>.</summary>
    public static Rgba32 LemonChiffon => new(0xFF, 0xFA, 0xCD);

    /// <summary>Gets the color <c>#ADD8E6</c>.</summary>
    public static Rgba32 LightBlue => new(0xAD, 0xD8, 0xE6);

    /// <summary>Gets the color <c>#F08080</c>.</summary>
    public static Rgba32 LightCoral => new(0xF0, 0x80, 0x80);

    /// <summary>Gets the color <c>#E0FFFF</c>.</summary>
    public static Rgba32 LightCyan => new(0xE0, 0xFF, 0xFF);

    /// <summary>Gets the color <c>#FAFAD2</c>.</summary>
    public static Rgba32 LightGoldenrodYellow => new(0xFA, 0xFA, 0xD2);

    /// <summary>Gets the color <c>#D3D3D3</c>.</summary>
    public static Rgba32 LightGray => new(0xD3, 0xD3, 0xD3);

    /// <summary>Gets the color <c>#90EE90</c>.</summary>
    public static Rgba32 LightGreen => new(0x90, 0xEE, 0x90);

    /// <summary>Gets the color <c>#FFB6C1</c>.</summary>
    public static Rgba32 LightPink => new(0xFF, 0xB6, 0xC1);

    /// <summary>Gets the color <c>#FFA07A</c>.</summary>
    public static Rgba32 LightSalmon => new(0xFF, 0xA0, 0x7A);

    /// <summary>Gets the color <c>#20B2AA</c>.</summary>
    public static Rgba32 LightSeaGreen => new(0x20, 0xB2, 0xAA);

    /// <summary>Gets the color <c>#87CEFA</c>.</summary>
    public static Rgba32 LightSkyBlue => new(0x87, 0xCE, 0xFA);

    /// <summary>Gets the color <c>#778899</c>.</summary>
    public static Rgba32 LightSlateGray => new(0x77, 0x88, 0x99);

    /// <summary>Gets the color <c>#B0C4DE</c>.</summary>
    public static Rgba32 LightSteelBlue => new(0xB0, 0xC4, 0xDE);

    /// <summary>Gets the color <c>#FFFFE0</c>.</summary>
    public static Rgba32 LightYellow => new(0xFF, 0xFF, 0xE0);

    /// <summary>Gets the color <c>#00FF00</c>.</summary>
    public static Rgba32 Lime => new(0x00, 0xFF, 0x00);

    /// <summary>Gets the color <c>#32CD32</c>.</summary>
    public static Rgba32 LimeGreen => new(0x32, 0xCD, 0x32);

    /// <summary>Gets the color <c>#FAF0E6</c>.</summary>
    public static Rgba32 Linen => new(0xFA, 0xF0, 0xE6);

    /// <summary>Gets the color <c>#FF00FF</c>.</summary>
    public static Rgba32 Magenta => new(0xFF, 0x00, 0xFF);

    /// <summary>Gets the color <c>#800000</c>.</summary>
    public static Rgba32 Maroon => new(0x80, 0x00, 0x00);

    /// <summary>Gets the color <c>#66CDAA</c>.</summary>
    public static Rgba32 MediumAquamarine => new(0x66, 0xCD, 0xAA);

    /// <summary>Gets the color <c>#0000CD</c>.</summary>
    public static Rgba32 MediumBlue => new(0x00, 0x00, 0xCD);

    /// <summary>Gets the color <c>#BA55D3</c>.</summary>
    public static Rgba32 MediumOrchid => new(0xBA, 0x55, 0xD3);

    /// <summary>Gets the color <c>#9370DB</c>.</summary>
    public static Rgba32 MediumPurple => new(0x93, 0x70, 0xDB);

    /// <summary>Gets the color <c>#3CB371</c>.</summary>
    public static Rgba32 MediumSeaGreen => new(0x3C, 0xB3, 0x71);

    /// <summary>Gets the color <c>#7B68EE</c>.</summary>
    public static Rgba32 MediumSlateBlue => new(0x7B, 0x68, 0xEE);

    /// <summary>Gets the color <c>#00FA9A</c>.</summary>
    public static Rgba32 MediumSpringGreen => new(0x00, 0xFA, 0x9A);

    /// <summary>Gets the color <c>#48D1CC</c>.</summary>
    public static Rgba32 MediumTurquoise => new(0x48, 0xD1, 0xCC);

    /// <summary>Gets the color <c>#C71585</c>.</summary>
    public static Rgba32 MediumVioletRed => new(0xC7, 0x15, 0x85);

    /// <summary>Gets the color <c>#191970</c>.</summary>
    public static Rgba32 MidnightBlue => new(0x19, 0x19, 0x70);

    /// <summary>Gets the color <c>#F5FFFA</c>.</summary>
    public static Rgba32 MintCream => new(0xF5, 0xFF, 0xFA);

    /// <summary>Gets the color <c>#FFE4E1</c>.</summary>
    public static Rgba32 MistyRose => new(0xFF, 0xE4, 0xE1);

    /// <summary>Gets the color <c>#FFE4B5</c>.</summary>
    public static Rgba32 Moccasin => new(0xFF, 0xE4, 0xB5);

    /// <summary>Gets the color <c>#FFDEAD</c>.</summary>
    public static Rgba32 NavajoWhite => new(0xFF, 0xDE, 0xAD);

    /// <summary>Gets the color <c>#000080</c>.</summary>
    public static Rgba32 Navy => new(0x00, 0x00, 0x80);

    /// <summary>Gets the color <c>#FDF5E6</c>.</summary>
    public static Rgba32 OldLace => new(0xFD, 0xF5, 0xE6);

    /// <summary>Gets the color <c>#808000</c>.</summary>
    public static Rgba32 Olive => new(0x80, 0x80, 0x00);

    /// <summary>Gets the color <c>#6B8E23</c>.</summary>
    public static Rgba32 OliveDrab => new(0x6B, 0x8E, 0x23);

    /// <summary>Gets the color <c>#FFA500</c>.</summary>
    public static Rgba32 Orange => new(0xFF, 0xA5, 0x00);

    /// <summary>Gets the color <c>#FF4500</c>.</summary>
    public static Rgba32 OrangeRed => new(0xFF, 0x45, 0x00);

    /// <summary>Gets the color <c>#DA70D6</c>.</summary>
    public static Rgba32 Orchid => new(0xDA, 0x70, 0xD6);

    /// <summary>Gets the color <c>#EEE8AA</c>.</summary>
    public static Rgba32 PaleGoldenrod => new(0xEE, 0xE8, 0xAA);

    /// <summary>Gets the color <c>#98FB98</c>.</summary>
    public static Rgba32 PaleGreen => new(0x98, 0xFB, 0x98);

    /// <summary>Gets the color <c>#AFEEEE</c>.</summary>
    public static Rgba32 PaleTurquoise => new(0xAF, 0xEE, 0xEE);

    /// <summary>Gets the color <c>#DB7093</c>.</summary>
    public static Rgba32 PaleVioletRed => new(0xDB, 0x70, 0x93);

    /// <summary>Gets the color <c>#FFEFD5</c>.</summary>
    public static Rgba32 PapayaWhip => new(0xFF, 0xEF, 0xD5);

    /// <summary>Gets the color <c>#FFDAB9</c>.</summary>
    public static Rgba32 PeachPuff => new(0xFF, 0xDA, 0xB9);

    /// <summary>Gets the color <c>#CD853F</c>.</summary>
    public static Rgba32 Peru => new(0xCD, 0x85, 0x3F);

    /// <summary>Gets the color <c>#FFC0CB</c>.</summary>
    public static Rgba32 Pink => new(0xFF, 0xC0, 0xCB);

    /// <summary>Gets the color <c>#DDA0DD</c>.</summary>
    public static Rgba32 Plum => new(0xDD, 0xA0, 0xDD);

    /// <summary>Gets the color <c>#B0E0E6</c>.</summary>
    public static Rgba32 PowderBlue => new(0xB0, 0xE0, 0xE6);

    /// <summary>Gets the color <c>#800080</c>.</summary>
    public static Rgba32 Purple => new(0x80, 0x00, 0x80);

    /// <summary>Gets the color <c>#663399</c>.</summary>
    public static Rgba32 RebeccaPurple => new(0x66, 0x33, 0x99);

    /// <summary>Gets the color <c>#FF0000</c>.</summary>
    public static Rgba32 Red => new(0xFF, 0x00, 0x00);

    /// <summary>Gets the color <c>#BC8F8F</c>.</summary>
    public static Rgba32 RosyBrown => new(0xBC, 0x8F, 0x8F);

    /// <summary>Gets the color <c>#4169E1</c>.</summary>
    public static Rgba32 RoyalBlue => new(0x41, 0x69, 0xE1);

    /// <summary>Gets the color <c>#8B4513</c>.</summary>
    public static Rgba32 SaddleBrown => new(0x8B, 0x45, 0x13);

    /// <summary>Gets the color <c>#FA8072</c>.</summary>
    public static Rgba32 Salmon => new(0xFA, 0x80, 0x72);

    /// <summary>Gets the color <c>#F4A460</c>.</summary>
    public static Rgba32 SandyBrown => new(0xF4, 0xA4, 0x60);

    /// <summary>Gets the color <c>#2E8B57</c>.</summary>
    public static Rgba32 SeaGreen => new(0x2E, 0x8B, 0x57);

    /// <summary>Gets the color <c>#FFF5EE</c>.</summary>
    public static Rgba32 SeaShell => new(0xFF, 0xF5, 0xEE);

    /// <summary>Gets the color <c>#A0522D</c>.</summary>
    public static Rgba32 Sienna => new(0xA0, 0x52, 0x2D);

    /// <summary>Gets the color <c>#C0C0C0</c>.</summary>
    public static Rgba32 Silver => new(0xC0, 0xC0, 0xC0);

    /// <summary>Gets the color <c>#87CEEB</c>.</summary>
    public static Rgba32 SkyBlue => new(0x87, 0xCE, 0xEB);

    /// <summary>Gets the color <c>#6A5ACD</c>.</summary>
    public static Rgba32 SlateBlue => new(0x6A, 0x5A, 0xCD);

    /// <summary>Gets the color <c>#708090</c>.</summary>
    public static Rgba32 SlateGray => new(0x70, 0x80, 0x90);

    /// <summary>Gets the color <c>#FFFAFA</c>.</summary>
    public static Rgba32 Snow => new(0xFF, 0xFA, 0xFA);

    /// <summary>Gets the color <c>#00FF7F</c>.</summary>
    public static Rgba32 SpringGreen => new(0x00, 0xFF, 0x7F);

    /// <summary>Gets the color <c>#4682B4</c>.</summary>
    public static Rgba32 SteelBlue => new(0x46, 0x82, 0xB4);

    /// <summary>Gets the color <c>#D2B48C</c>.</summary>
    public static Rgba32 Tan => new(0xD2, 0xB4, 0x8C);

    /// <summary>Gets the color <c>#008080</c>.</summary>
    public static Rgba32 Teal => new(0x00, 0x80, 0x80);

    /// <summary>Gets the color <c>#D8BFD8</c>.</summary>
    public static Rgba32 Thistle => new(0xD8, 0xBF, 0xD8);

    /// <summary>Gets the color <c>#FF6347</c>.</summary>
    public static Rgba32 Tomato => new(0xFF, 0x63, 0x47);

    /// <summary>Gets the color <c>#40E0D0</c>.</summary>
    public static Rgba32 Turquoise => new(0x40, 0xE0, 0xD0);

    /// <summary>Gets the color <c>#EE82EE</c>.</summary>
    public static Rgba32 Violet => new(0xEE, 0x82, 0xEE);

    /// <summary>Gets the color <c>#F5DEB3</c>.</summary>
    public static Rgba32 Wheat => new(0xF5, 0xDE, 0xB3);

    /// <summary>Gets the color <c>#FFFFFF</c>.</summary>
    public static Rgba32 White => new(0xFF, 0xFF, 0xFF);

    /// <summary>Gets the color <c>#F5F5F5</c>.</summary>
    public static Rgba32 WhiteSmoke => new(0xF5, 0xF5, 0xF5);

    /// <summary>Gets the color <c>#FFFF00</c>.</summary>
    public static Rgba32 Yellow => new(0xFF, 0xFF, 0x00);

    /// <summary>Gets the color <c>#9ACD32</c>.</summary>
    public static Rgba32 YellowGreen => new(0x9A, 0xCD, 0x32);
}
