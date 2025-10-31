namespace Meziantou.Framework.Diagnostics.ContextSnapshot;

/// <summary>Represents a snapshot of culture settings at a specific point in time.</summary>
public sealed class CultureSnapshot
{
    internal CultureSnapshot()
    {
        GlobalizationInvariant = IsGlobalizationInvariant();
        if (AppContext.TryGetSwitch("System.Globalization.PredefinedCulturesOnly", out var usePredefinedCulturesOnly))
        {
            UsePredefinedCulturesOnly = usePredefinedCulturesOnly;
        }

        CurrentCulture = CultureInfoSnapshot.Get(CultureInfo.CurrentCulture);
        CurrentUICulture = CultureInfoSnapshot.Get(CultureInfo.CurrentUICulture);
        DefaultThreadCurrentCulture = CultureInfoSnapshot.Get(CultureInfo.DefaultThreadCurrentCulture);
        DefaultThreadCurrentUICulture = CultureInfoSnapshot.Get(CultureInfo.DefaultThreadCurrentUICulture);
    }

    /// <summary>Gets a value indicating whether globalization invariant mode is enabled.</summary>
    public bool GlobalizationInvariant { get; }
    /// <summary>Gets a value indicating whether only predefined cultures are used.</summary>
    public bool? UsePredefinedCulturesOnly { get; }
    /// <summary>Gets the current culture used for formatting and parsing.</summary>
    public CultureInfoSnapshot? CurrentCulture { get; }
    /// <summary>Gets the current UI culture used for resource lookup.</summary>
    public CultureInfoSnapshot? CurrentUICulture { get; }
    /// <summary>Gets the default culture for new threads.</summary>
    public CultureInfoSnapshot? DefaultThreadCurrentCulture { get; }
    /// <summary>Gets the default UI culture for new threads.</summary>
    public CultureInfoSnapshot? DefaultThreadCurrentUICulture { get; }

    private static bool IsGlobalizationInvariant()
    {
        // Validate the AppContext switch first
        if (AppContext.TryGetSwitch("System.Globalization.Invariant", out var isEnabled) && isEnabled)
            return true;

        // Then, check the environment variable
        var environmentValue = Environment.GetEnvironmentVariable("DOTNET_SYSTEM_GLOBALIZATION_INVARIANT");
        if (string.Equals(environmentValue, bool.TrueString, StringComparison.OrdinalIgnoreCase) || environmentValue == "1")
            return true;

        // .NET 6 and greater will throw if the culture is not found, unless PredefinedCulturesOnly is set to false.
        // Previous versions will return the Invariant Culture. See the breaking change for more info:
        // https://learn.microsoft.com/en-us/dotnet/core/compatibility/globalization/6.0/culture-creation-invariant-mode?WT.mc_id=DT-MVP-5003978
        //
        // You can detect if a culture is the Invariant culture by checking its name or one of its properties.
        // Note: The Invariant Culture is hard-coded: https://github.com/dotnet/runtime/blob/b69fa275565ceeca8ba39f7f9bcb1e301dd68ded/src/libraries/System.Private.CoreLib/src/System/Globalization/CultureData.cs#L547
        try
        {
            return CultureInfo.GetCultureInfo("en-US").EnglishName.Contains("Invariant", StringComparison.Ordinal);

            // Note: A user can change the settings of the culture at the OS level or ICU data, so the following comparison may be true even if the Invariant Mode is disabled.
            // return CultureInfo.GetCultureInfo("en-US").NumberFormat.CurrencySymbol == "¤";
        }
        catch (CultureNotFoundException)
        {
            // PredefinedCulturesOnly is true and the Invariant Mode is enabled
            return true;
        }
    }
}
