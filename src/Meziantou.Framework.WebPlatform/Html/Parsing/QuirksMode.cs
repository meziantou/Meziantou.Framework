using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Html.Parsing;

/// <summary>Quirks mode determination from the DOCTYPE (the "initial" insertion mode).</summary>
internal static class QuirksMode
{
    private static readonly string[] QuirksPublicIdPrefixes =
    [
        "+//Silmaril//dtd html Pro v0r11 19970101//",
        "-//AS//DTD HTML 3.0 asWedit + extensions//",
        "-//AdvaSoft Ltd//DTD HTML 3.0 asWedit + extensions//",
        "-//IETF//DTD HTML 2.0 Level 1//",
        "-//IETF//DTD HTML 2.0 Level 2//",
        "-//IETF//DTD HTML 2.0 Strict Level 1//",
        "-//IETF//DTD HTML 2.0 Strict Level 2//",
        "-//IETF//DTD HTML 2.0 Strict//",
        "-//IETF//DTD HTML 2.0//",
        "-//IETF//DTD HTML 2.1E//",
        "-//IETF//DTD HTML 3.0//",
        "-//IETF//DTD HTML 3.2 Final//",
        "-//IETF//DTD HTML 3.2//",
        "-//IETF//DTD HTML 3//",
        "-//IETF//DTD HTML Level 0//",
        "-//IETF//DTD HTML Level 1//",
        "-//IETF//DTD HTML Level 2//",
        "-//IETF//DTD HTML Level 3//",
        "-//IETF//DTD HTML Strict Level 0//",
        "-//IETF//DTD HTML Strict Level 1//",
        "-//IETF//DTD HTML Strict Level 2//",
        "-//IETF//DTD HTML Strict Level 3//",
        "-//IETF//DTD HTML Strict//",
        "-//IETF//DTD HTML//",
        "-//Metrius//DTD Metrius Presentational//",
        "-//Microsoft//DTD Internet Explorer 2.0 HTML Strict//",
        "-//Microsoft//DTD Internet Explorer 2.0 HTML//",
        "-//Microsoft//DTD Internet Explorer 2.0 Tables//",
        "-//Microsoft//DTD Internet Explorer 3.0 HTML Strict//",
        "-//Microsoft//DTD Internet Explorer 3.0 HTML//",
        "-//Microsoft//DTD Internet Explorer 3.0 Tables//",
        "-//Netscape Comm. Corp.//DTD HTML//",
        "-//Netscape Comm. Corp.//DTD Strict HTML//",
        "-//O'Reilly and Associates//DTD HTML 2.0//",
        "-//O'Reilly and Associates//DTD HTML Extended 1.0//",
        "-//O'Reilly and Associates//DTD HTML Extended Relaxed 1.0//",
        "-//SQ//DTD HTML 2.0 HoTMetaL + extensions//",
        "-//SoftQuad Software//DTD HoTMetaL PRO 6.0::19990601::extensions to HTML 4.0//",
        "-//SoftQuad//DTD HoTMetaL PRO 4.0::19971010::extensions to HTML 4.0//",
        "-//Spyglass//DTD HTML 2.0 Extended//",
        "-//Sun Microsystems Corp.//DTD HotJava HTML//",
        "-//Sun Microsystems Corp.//DTD HotJava Strict HTML//",
        "-//W3C//DTD HTML 3 1995-03-24//",
        "-//W3C//DTD HTML 3.2 Draft//",
        "-//W3C//DTD HTML 3.2 Final//",
        "-//W3C//DTD HTML 3.2//",
        "-//W3C//DTD HTML 3.2S Draft//",
        "-//W3C//DTD HTML 4.0 Frameset//",
        "-//W3C//DTD HTML 4.0 Transitional//",
        "-//W3C//DTD HTML Experimental 19960712//",
        "-//W3C//DTD HTML Experimental 970421//",
        "-//W3C//DTD W3 HTML//",
        "-//W3O//DTD W3 HTML 3.0//",
        "-//WebTechs//DTD Mozilla HTML 2.0//",
        "-//WebTechs//DTD Mozilla HTML//",
    ];

    private static bool StartsWithIgnoreCase(string value, string prefix) => value.Length >= prefix.Length && InfraAscii.EqualsIgnoreAsciiCase(value.AsSpan(0, prefix.Length), prefix);

    private static bool EqualsIgnoreCase(string? value, string other) => value is not null && InfraAscii.EqualsIgnoreAsciiCase(value, other);

    public static bool IsQuirks(bool forceQuirks, string? name, string? publicId, string? systemId)
    {
        if (forceQuirks || name != "html")
            return true;

        if (EqualsIgnoreCase(publicId, "-//W3O//DTD W3 HTML Strict 3.0//EN//") || EqualsIgnoreCase(publicId, "-/W3C/DTD HTML 4.0 Transitional/EN") || EqualsIgnoreCase(publicId, "HTML"))
            return true;

        if (EqualsIgnoreCase(systemId, "http://www.ibm.com/data/dtd/v11/ibmxhtml1-transitional.dtd"))
            return true;

        if (publicId is null)
            return false;

        foreach (var prefix in QuirksPublicIdPrefixes)
        {
            if (StartsWithIgnoreCase(publicId, prefix))
                return true;
        }

        if (string.IsNullOrEmpty(systemId) && (StartsWithIgnoreCase(publicId, "-//W3C//DTD HTML 4.01 Frameset//") || StartsWithIgnoreCase(publicId, "-//W3C//DTD HTML 4.01 Transitional//")))
            return true;

        return false;
    }

    public static bool IsLimitedQuirks(string? publicId, string? systemId)
    {
        if (publicId is null)
            return false;

        if (StartsWithIgnoreCase(publicId, "-//W3C//DTD XHTML 1.0 Frameset//") || StartsWithIgnoreCase(publicId, "-//W3C//DTD XHTML 1.0 Transitional//"))
            return true;

        return !string.IsNullOrEmpty(systemId) && (StartsWithIgnoreCase(publicId, "-//W3C//DTD HTML 4.01 Frameset//") || StartsWithIgnoreCase(publicId, "-//W3C//DTD HTML 4.01 Transitional//"));
    }
}
