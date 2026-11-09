using System.Globalization;

namespace Meziantou.Framework.Diagrams.Mermaid.Internals;

/// <summary>Builds the theme of a diagram from the <c>theme</c> and <c>themeVariables</c> of the configuration.</summary>
internal static class MermaidTheme
{
    public static void Apply(Diagram diagram, ParseContext context)
    {
        var configuration = context.Configuration;
        var name = configuration.GetString("theme")?.Trim();
        var theme = context.Options.DefaultTheme ?? DiagramTheme.Default;
        var isBase = false;
        switch (name)
        {
            case null or "" or "default":
                break;
            case "dark":
                theme = DiagramTheme.Dark;
                break;
            case "forest":
                theme = DiagramTheme.Forest;
                break;
            case "neutral":
                theme = DiagramTheme.Neutral;
                break;
            case "base":
                theme = DiagramTheme.Base;
                isBase = true;
                break;
            case "null":
                break;
            default:
                context.Warn(0, $"The theme '{name}' is not supported; the default theme is used.");
                if (name.Contains("dark", StringComparison.OrdinalIgnoreCase))
                {
                    theme = DiagramTheme.Dark;
                }

                break;
        }

        var variables = new Dictionary<string, string>(StringComparer.Ordinal);
        if (configuration.GetMap("themeVariables") is { } map)
        {
            foreach (var (key, value) in map)
            {
                if (value is string text)
                {
                    variables[key] = text;
                }
            }
        }

        theme = isBase ? DeriveBase(variables, context) : Override(theme, variables, context);
        if (configuration.GetString("fontFamily") is { Length: > 0 } fontFamily)
        {
            theme = theme with { FontFamily = fontFamily.Trim().TrimEnd(';') };
        }

        if (configuration.GetNumber("fontSize") is > 0 and < 200 and var fontSize)
        {
            theme = theme with { FontSize = fontSize };
        }

        if (configuration.GetString("look") is "handDrawn")
        {
            context.Warn(0, "The hand-drawn look is not supported; the classic look is used.");
        }

        diagram.Theme = theme;
    }

    private static DiagramTheme Override(DiagramTheme theme, Dictionary<string, string> variables, ParseContext context)
    {
        foreach (var (name, value) in variables)
        {
            if (name is "fontFamily")
            {
                theme = theme with { FontFamily = value.Trim().TrimEnd(';') };
                continue;
            }

            if (name is "fontSize")
            {
                if (ParseSize(value) is > 0 and < 200 and var size)
                {
                    theme = theme with { FontSize = size };
                }

                continue;
            }

            if (name is "darkMode")
                continue;

            if (!SceneColor.TryParse(value, out var color))
            {
                context.Warn(0, $"The value '{value}' of the theme variable '{name}' is not a supported color.");
                continue;
            }

            theme = Set(theme, name, color);
        }

        return theme;
    }

    private static DiagramTheme Set(DiagramTheme theme, string name, SceneColor color)
    {
        if (name.StartsWith("cScale", StringComparison.Ordinal) && int.TryParse(name.AsSpan(6), NumberStyles.None, CultureInfo.InvariantCulture, out var index) && index is >= 0 and < 12)
        {
            var palette = theme.Palette.Count == 12 ? theme.Palette.ToArray() : new SceneColor[12];
            palette[index] = color;
            return theme with { Palette = palette };
        }

        return name switch
        {
            "background" => theme with { Background = color },
            "primaryColor" => theme with { PrimaryColor = color },
            "primaryBorderColor" => theme with { PrimaryBorderColor = color },
            "primaryTextColor" => theme with { PrimaryTextColor = color },
            "secondaryColor" => theme with { SecondaryColor = color },
            "secondaryBorderColor" => theme with { SecondaryBorderColor = color },
            "secondaryTextColor" => theme with { SecondaryTextColor = color },
            "tertiaryColor" => theme with { TertiaryColor = color },
            "tertiaryBorderColor" => theme with { TertiaryBorderColor = color },
            "tertiaryTextColor" => theme with { TertiaryTextColor = color },
            "mainBkg" or "nodeBkg" => theme with { NodeBackground = color },
            "nodeBorder" => theme with { NodeBorderColor = color },
            "nodeTextColor" => theme with { NodeTextColor = color },
            "lineColor" or "defaultLinkColor" => theme with { LineColor = color },
            "arrowheadColor" => theme with { ArrowheadColor = color },
            "textColor" => theme with { TextColor = color },
            "titleColor" => theme with { TitleColor = color },
            "edgeLabelBackground" => theme with { EdgeLabelBackground = color },
            "clusterBkg" => theme with { ClusterBackground = color },
            "clusterBorder" => theme with { ClusterBorderColor = color },
            "noteBkgColor" => theme with { NoteBackground = color },
            "noteBorderColor" => theme with { NoteBorderColor = color },
            "noteTextColor" => theme with { NoteTextColor = color },
            "rowOdd" or "attributeBackgroundColorOdd" => theme with { RowOddBackground = color },
            "rowEven" or "attributeBackgroundColorEven" => theme with { RowEvenBackground = color },
            "specialStateColor" => theme with { SpecialStateColor = color },
            "innerEndBackground" => theme with { InnerEndBackground = color },
            "compositeBackground" => theme with { CompositeBackground = color },
            "compositeTitleBackground" => theme with { CompositeTitleBackground = color },
            "altBackground" => theme with { AlternateBackground = color },
            _ => theme,
        };
    }

    /// <summary>The base theme of Mermaid: every color derives from <c>primaryColor</c>, <c>background</c> and <c>darkMode</c>, unless it is set.</summary>
    private static DiagramTheme DeriveBase(Dictionary<string, string> variables, ParseContext context)
    {
        var darkMode = variables.TryGetValue("darkMode", out var dark) && string.Equals(dark.Trim(), "true", StringComparison.OrdinalIgnoreCase);
        var resolved = new Dictionary<string, SceneColor>(StringComparer.Ordinal);
        foreach (var (name, value) in variables)
        {
            if (name is "fontFamily" or "fontSize" or "darkMode")
                continue;

            if (SceneColor.TryParse(value, out var color))
            {
                resolved[name] = color;
            }
            else
            {
                context.Warn(0, $"The value '{value}' of the theme variable '{name}' is not a supported color.");
            }
        }

        SceneColor Get(string name, Func<SceneColor> fallback)
        {
            if (!resolved.TryGetValue(name, out var color))
            {
                color = fallback();
                resolved[name] = color;
            }

            return color;
        }

        var background = Get("background", () => SceneColor.Parse("#f4f4f4"));
        var primary = Get("primaryColor", () => SceneColor.Parse("#fff4dd"));
        var noteBackground = Get("noteBkgColor", () => SceneColor.Parse("#fff5ad"));
        var noteText = Get("noteTextColor", () => SceneColor.Parse("#333333"));
        var primaryText = Get("primaryTextColor", () => SceneColor.Parse(darkMode ? "#eeeeee" : "#333333"));
        var secondary = Get("secondaryColor", () => ColorMath.Adjust(primary, hue: -120));
        var tertiary = Get("tertiaryColor", () => ColorMath.Adjust(primary, hue: 180, lightness: 5));
        var primaryBorder = Get("primaryBorderColor", () => ColorMath.MakeBorder(primary, darkMode));
        var secondaryBorder = Get("secondaryBorderColor", () => ColorMath.MakeBorder(secondary, darkMode));
        var tertiaryBorder = Get("tertiaryBorderColor", () => ColorMath.MakeBorder(tertiary, darkMode));
        var noteBorder = Get("noteBorderColor", () => ColorMath.MakeBorder(noteBackground, darkMode));
        var secondaryText = Get("secondaryTextColor", () => ColorMath.Invert(secondary));
        var tertiaryText = Get("tertiaryTextColor", () => ColorMath.Invert(tertiary));
        var line = Get("lineColor", () => ColorMath.Invert(background));
        var arrowhead = Get("arrowheadColor", () => ColorMath.Invert(background));
        var text = Get("textColor", () => primaryText);
        var mainBackground = Get("mainBkg", () => resolved.TryGetValue("nodeBkg", out var nodeBackground) ? nodeBackground : primary);
        var palette = new SceneColor[12];
        int[] hues = [0, -120, 180, 30, 60, 90, 120, 150, 210, 270, 300, 330];
        for (var i = 0; i < palette.Length; i++)
        {
            var index = i;
            palette[i] = Get("cScale" + i.ToString(CultureInfo.InvariantCulture), () =>
            {
                var source = index switch
                {
                    0 => primary,
                    1 => secondary,
                    2 => tertiary,
                    8 => ColorMath.Adjust(primary, hue: hues[index], lightness: 150),
                    _ => ColorMath.Adjust(primary, hue: hues[index]),
                };
                return darkMode ? ColorMath.Adjust(source, lightness: 25) : ColorMath.Adjust(source, lightness: -25);
            });
        }

        var theme = DiagramTheme.Base with
        {
            Background = background,
            IsDark = darkMode,
            PrimaryColor = primary,
            PrimaryBorderColor = primaryBorder,
            PrimaryTextColor = primaryText,
            SecondaryColor = secondary,
            SecondaryBorderColor = secondaryBorder,
            SecondaryTextColor = secondaryText,
            TertiaryColor = tertiary,
            TertiaryBorderColor = tertiaryBorder,
            TertiaryTextColor = tertiaryText,
            NodeBackground = mainBackground,
            NodeBorderColor = Get("nodeBorder", () => primaryBorder),
            NodeTextColor = Get("nodeTextColor", () => primaryText),
            LineColor = line,
            ArrowheadColor = arrowhead,
            TextColor = text,
            TitleColor = Get("titleColor", () => tertiaryText),
            EdgeLabelBackground = Get("edgeLabelBackground", () => darkMode ? ColorMath.Adjust(secondary, lightness: -30) : secondary),
            ClusterBackground = Get("clusterBkg", () => tertiary),
            ClusterBorderColor = Get("clusterBorder", () => tertiaryBorder),
            NoteBackground = noteBackground,
            NoteBorderColor = noteBorder,
            NoteTextColor = noteText,
            RowOddBackground = Get("rowOdd", () => darkMode ? ColorMath.Adjust(mainBackground, lightness: -5) : ColorMath.Adjust(mainBackground, lightness: 75)),
            RowEvenBackground = Get("rowEven", () => darkMode ? ColorMath.Adjust(mainBackground, lightness: -10) : ColorMath.Adjust(mainBackground, lightness: 5)),
            SpecialStateColor = Get("specialStateColor", () => line),
            InnerEndBackground = Get("innerEndBackground", () => primaryBorder),
            CompositeBackground = Get("compositeBackground", () => background),
            CompositeTitleBackground = Get("compositeTitleBackground", () => mainBackground),
            AlternateBackground = Get("altBackground", () => tertiary),
            Palette = palette,
        };

        if (variables.TryGetValue("fontFamily", out var fontFamily))
        {
            theme = theme with { FontFamily = fontFamily.Trim().TrimEnd(';') };
        }

        if (variables.TryGetValue("fontSize", out var fontSizeText) && ParseSize(fontSizeText) is > 0 and < 200 and var fontSize)
        {
            theme = theme with { FontSize = fontSize };
        }

        return theme;
    }

    private static double? ParseSize(string value)
    {
        var text = value.Trim();
        if (text.EndsWith("px", StringComparison.OrdinalIgnoreCase))
        {
            text = text[..^2];
        }

        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var size) && double.IsFinite(size) ? size : null;
    }
}
