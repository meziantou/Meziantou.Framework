namespace Meziantou.Framework.JavaScriptEngine.Intl;

/// <summary>An Intl.Locale instance: an ordinary object with the [[InitializedLocale]] internal slot.</summary>
internal sealed class JsLocaleObject(JsObject prototype) : JsObject(prototype, ObjectClass.Intl)
{
    /// <summary>[[Locale]]: the canonical locale identifier.</summary>
    public string Locale { get; set; } = "";

    /// <summary>[[Calendar]], or null (undefined).</summary>
    public string? Calendar { get; set; }

    /// <summary>[[Collation]], or null (undefined).</summary>
    public string? Collation { get; set; }

    /// <summary>[[FirstDayOfWeek]], or null (undefined).</summary>
    public string? FirstDayOfWeek { get; set; }

    /// <summary>[[HourCycle]], or null (undefined).</summary>
    public string? HourCycle { get; set; }

    /// <summary>[[CaseFirst]], or null (undefined).</summary>
    public string? CaseFirst { get; set; }

    /// <summary>[[Numeric]]</summary>
    public bool Numeric { get; set; }

    /// <summary>[[NumberingSystem]], or null (undefined).</summary>
    public string? NumberingSystem { get; set; }
}
