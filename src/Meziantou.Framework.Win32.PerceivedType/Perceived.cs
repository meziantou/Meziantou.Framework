using System.Collections.Concurrent;
using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace Meziantou.Framework.Win32;

/// <summary>
/// Defines a file's perceived type based on its extension.
/// </summary>
/// <example>
/// <code>
/// // Get perceived type for a file
/// var perceived = Perceived.GetPerceivedType(".txt");
/// Console.WriteLine(perceived.PerceivedType); // Output: Text
/// Console.WriteLine(perceived.PerceivedTypeSource); // Output: SoftCoded or HardCoded
///
/// // Add custom perceived types
/// Perceived.AddPerceived(".myext", PerceivedType.Text);
///
/// // Add default perceived types for common development file extensions
/// Perceived.AddDefaultPerceivedTypes();
/// </code>
/// </example>
public sealed class Perceived
{
    private static readonly ConcurrentDictionary<string, Perceived> PerceivedTypes = new(StringComparer.OrdinalIgnoreCase);

    private static object SyncObject { get; } = new object();

    private Perceived(string extension, PerceivedType perceivedType, PerceivedTypeSource perceivedTypeSource)
    {
        Extension = extension;
        PerceivedType = perceivedType;
        PerceivedTypeSource = perceivedTypeSource;
    }

    /// <summary>Adds default perceived types for common development file extensions such as .cs, .html, .xaml, .sln, etc.</summary>
    public static void AddDefaultPerceivedTypes()
    {
        AddPerceived(".appxmanifest", PerceivedType.Text);
        AddPerceived(".asax", PerceivedType.Text);
        AddPerceived(".ascx", PerceivedType.Text);
        AddPerceived(".ashx", PerceivedType.Text);
        AddPerceived(".asmx", PerceivedType.Text);
        AddPerceived(".bat", PerceivedType.Text);
        AddPerceived(".class", PerceivedType.Text);
        AddPerceived(".cmd", PerceivedType.Text);
        AddPerceived(".cs", PerceivedType.Text);
        AddPerceived(".cshtml", PerceivedType.Text);
        AddPerceived(".css", PerceivedType.Text);
        AddPerceived(".cfxproj", PerceivedType.Text);
        AddPerceived(".config", PerceivedType.Text);
        AddPerceived(".csproj", PerceivedType.Text);
        AddPerceived(".dll", PerceivedType.Application);
        AddPerceived(".exe", PerceivedType.Application);
        AddPerceived(".htm", PerceivedType.Text);
        AddPerceived(".html", PerceivedType.Text);
        AddPerceived(".iqy", PerceivedType.Text);
        AddPerceived(".js", PerceivedType.Text);
        AddPerceived(".master", PerceivedType.Text);
        AddPerceived(".manifest", PerceivedType.Text);
        AddPerceived(".rdl", PerceivedType.Text);
        AddPerceived(".reg", PerceivedType.Text);
        AddPerceived(".resx", PerceivedType.Text);
        AddPerceived(".rtf", PerceivedType.Text);
        AddPerceived(".rzt", PerceivedType.Text);
        AddPerceived(".sln", PerceivedType.Text);
        AddPerceived(".sql", PerceivedType.Text);
        AddPerceived(".sqlproj", PerceivedType.Text);
        AddPerceived(".snippet", PerceivedType.Text);
        AddPerceived(".svc", PerceivedType.Text);
        AddPerceived(".tpl", PerceivedType.Text);
        AddPerceived(".tplxaml", PerceivedType.Text);
        AddPerceived(".vb", PerceivedType.Text);
        AddPerceived(".vbhtml", PerceivedType.Text);
        AddPerceived(".vbproj", PerceivedType.Text);
        AddPerceived(".vbs", PerceivedType.Text);
        AddPerceived(".vdproj", PerceivedType.Text);
        AddPerceived(".wsdl", PerceivedType.Text);
        AddPerceived(".wxi", PerceivedType.Text);
        AddPerceived(".wxl", PerceivedType.Text);
        AddPerceived(".wxs", PerceivedType.Text);
        AddPerceived(".wixlib", PerceivedType.Text);
        AddPerceived(".xaml", PerceivedType.Text);
        AddPerceived(".xsd", PerceivedType.Text);
        AddPerceived(".xsl", PerceivedType.Text);
        AddPerceived(".xslt", PerceivedType.Text);
    }

    /// <summary>
    /// Adds a perceived instance to the list.
    /// </summary>
    /// <param name="extension">The file extension. May not be null.</param>
    /// <param name="type">The perceived type.</param>
    public static Perceived AddPerceived(string extension, PerceivedType type)
    {
        ArgumentNullException.ThrowIfNull(extension);

        var perceived = new Perceived(extension, type, PerceivedTypeSource.HardCoded);
        lock (SyncObject)
        {
            PerceivedTypes[perceived.Extension] = perceived;
        }

        return perceived;
    }

    /// <summary>
    /// Gets the file's extension.
    /// </summary>
    /// <value>The file's extension.</value>
    public string Extension { get; }

    /// <summary>
    /// Indicates the normalized perceived type.
    /// </summary>
    /// <value>The normalized perceived type.</value>
    public PerceivedType PerceivedType { get; }

    /// <summary>
    /// Indicates the source of the perceived type information.
    /// </summary>
    /// <value>the source of the perceived type information.</value>
    public PerceivedTypeSource PerceivedTypeSource { get; }

    /// <summary>
    /// Gets a file's perceived type based on its extension.
    /// </summary>
    /// <param name="fileName">The file name. May not be null..</param>
    /// <returns>An instance of the PerceivedType type.</returns>
    [SupportedOSPlatform("windows5.1.2600")]
    public static unsafe Perceived GetPerceivedType(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);

        var extension = Path.GetExtension(fileName) ?? throw new ArgumentException("The extension cannot be determined from the file name", nameof(fileName));

        extension = extension.ToUpperInvariant();
        if (PerceivedTypes.TryGetValue(extension, out var ptype))
            return ptype;

        if (!IsSupportedPlatform())
            throw new PlatformNotSupportedException("PerceivedType is only supported on Windows");

        lock (SyncObject)
        {
            var type = PerceivedType.Unknown;
            var source = PerceivedTypeSource.Undefined;
            if (!PerceivedTypes.TryGetValue(extension, out ptype))
            {
                source = PerceivedTypeSource.Undefined;
                var hr = PInvoke.AssocGetPerceivedType(extension, out var perceivedType, out var flag, out _);
                if (hr.Failed)
                {
                    type = PerceivedType.Unspecified;
                    source = PerceivedTypeSource.Undefined;
                }
                else
                {
                    type = (PerceivedType)perceivedType;
                    source = (PerceivedTypeSource)flag;
                }

                ptype = new Perceived(extension, type, source);
                PerceivedTypes.TryAdd(extension, ptype);
            }

            return ptype;
        }
    }

    /// <summary>
    /// Returns a <see cref="string"/> that represents the current <see cref="object"/>.
    /// </summary>
    /// <returns>
    /// A <see cref="string"/> that represents the current <see cref="object"/>.
    /// </returns>
    public override string ToString()
    {
        return Extension + ":" + PerceivedType + " (" + PerceivedTypeSource + ")";
    }

    private static bool IsSupportedPlatform()
    {
        return OperatingSystem.IsWindows();
    }
}
