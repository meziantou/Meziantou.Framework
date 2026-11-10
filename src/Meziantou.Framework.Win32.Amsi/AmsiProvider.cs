namespace Meziantou.Framework.Win32;

/// <summary>Describes an antimalware provider registered with the Antimalware Scan Interface (AMSI).</summary>
public sealed class AmsiProvider
{
    internal AmsiProvider(Guid id, string? displayName, string? modulePath)
    {
        Id = id;
        DisplayName = displayName;
        ModulePath = modulePath;
    }

    /// <summary>Gets the class identifier (CLSID) of the COM object that implements the provider.</summary>
    public Guid Id { get; }

    /// <summary>Gets the name of the COM class of the provider, or <see langword="null"/> when it is not registered.</summary>
    public string? DisplayName { get; }

    /// <summary>Gets the path of the DLL that implements the provider, or <see langword="null"/> when it is not registered.</summary>
    public string? ModulePath { get; }

    public override string ToString() => DisplayName ?? Id.ToString("B");
}
