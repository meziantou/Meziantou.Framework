using Meziantou.Framework.Yaml.Tokens;

namespace Meziantou.Framework.Yaml;

/// <summary>Defines constants that relate to the YAML specification.</summary>
internal static class Constants
{
    public static readonly TagDirective[] DefaultTagDirectives = new[]
    {
        new TagDirective("!", "!"),
        new TagDirective("!!", "tag:yaml.org,2002:"),
    };

    public const int MajorVersion = 1;
    public const int MinorVersion = 2;

    public static bool IsSupportedYamlVersion(Version version)
    {
        return version.Major == 1 && (version.Minor == 1 || version.Minor == 2);
    }

    public const char HandleCharacter = '!';
    public const string DefaultHandle = "!";
}