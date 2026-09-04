using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Meziantou.Framework.Diagnostics.ContextSnapshot.Internals;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.SecurityCenter;

namespace Meziantou.Framework.Diagnostics.ContextSnapshot;

/// <summary>Represents a snapshot of all security providers on the system including antivirus, firewall, and anti-spyware (Windows only).</summary>
public sealed class SecurityProvidersSnapshot
{
    internal SecurityProvidersSnapshot()
    {
    }

    public string? HealthStatus { get; } = Utils.SafeGet(GetHealthStatus);
    public ImmutableArray<SecurityProviderSnapshot> Antivirus { get; } = SafeGet(WSC_SECURITY_PROVIDER.WSC_SECURITY_PROVIDER_ANTIVIRUS);
    public ImmutableArray<SecurityProviderSnapshot> Firewall { get; } = SafeGet(WSC_SECURITY_PROVIDER.WSC_SECURITY_PROVIDER_FIREWALL);
    public ImmutableArray<SecurityProviderSnapshot> AntiSpyware { get; } = SafeGet(WSC_SECURITY_PROVIDER.WSC_SECURITY_PROVIDER_ANTISPYWARE);

    // Utils.SafeGet cannot be used here: on failure it would return a default ImmutableArray, which throws when enumerated.
    private static ImmutableArray<SecurityProviderSnapshot> SafeGet(WSC_SECURITY_PROVIDER provider)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(8))
            return ImmutableArray<SecurityProviderSnapshot>.Empty;

        try
        {
            return Get(provider);
        }
        catch
        {
            return ImmutableArray<SecurityProviderSnapshot>.Empty;
        }
    }

    [SupportedOSPlatform("windows8.0")]
    private static ImmutableArray<SecurityProviderSnapshot> Get(WSC_SECURITY_PROVIDER provider)
    {
        var wscProductListType = Type.GetTypeFromCLSID(typeof(WSCProductList).GUID, throwOnError: false);
        if (wscProductListType is null)
            return ImmutableArray<SecurityProviderSnapshot>.Empty;

        var wscProductList = Utils.SafeGet(() => Activator.CreateInstance(wscProductListType));
        if (wscProductList is null)
            return ImmutableArray<SecurityProviderSnapshot>.Empty;

        var pWSCProductList = (IWSCProductList)wscProductList;
        try
        {
            pWSCProductList.Initialize(provider);
            var nProductCount = pWSCProductList.Count;

            var products = ImmutableArray.CreateBuilder<SecurityProviderSnapshot>(initialCapacity: nProductCount);
            for (var i = 0u; i < (uint)nProductCount; i++)
            {
                string? productName = null;
                string? productState = null;
                string? productStatus = null;
                string? remediationPath = null;
                string? stateTimestamp = null;

                var index = i;
                var pWscProduct = Utils.SafeGet(() =>
                {
                    pWSCProductList.get_Item(index, out var product);
                    return product;
                });

                if (pWscProduct is not null)
                {
                    try
                    {
                        productName = GetString(() => pWscProduct.ProductName);
                        productState = Utils.SafeGet<WSC_SECURITY_PRODUCT_STATE?>(() => pWscProduct.ProductState) switch
                        {
                            WSC_SECURITY_PRODUCT_STATE.WSC_SECURITY_PRODUCT_STATE_ON => "On",
                            WSC_SECURITY_PRODUCT_STATE.WSC_SECURITY_PRODUCT_STATE_OFF => "Off",
                            WSC_SECURITY_PRODUCT_STATE.WSC_SECURITY_PRODUCT_STATE_SNOOZED => "Snoozed",
                            null => null,
                            _ => "Expired",
                        };

                        if (provider != WSC_SECURITY_PROVIDER.WSC_SECURITY_PROVIDER_FIREWALL)
                        {
                            productStatus = Utils.SafeGet<WSC_SECURITY_SIGNATURE_STATUS?>(() => pWscProduct.SignatureStatus) switch
                            {
                                WSC_SECURITY_SIGNATURE_STATUS.WSC_SECURITY_PRODUCT_UP_TO_DATE => "Up-to-date",
                                null => null,
                                _ => "Out-of-date",
                            };
                        }

                        remediationPath = GetString(() => pWscProduct.RemediationPath);
                        if (provider == WSC_SECURITY_PROVIDER.WSC_SECURITY_PROVIDER_ANTIVIRUS)
                        {
                            stateTimestamp = GetString(() => pWscProduct.ProductStateTimestamp);
                        }
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(pWscProduct);
                    }
                }

                products.Add(new SecurityProviderSnapshot(productName, remediationPath, productStatus, productState, stateTimestamp));
            }

            return products.ToImmutable();
        }
        finally
        {
            Marshal.ReleaseComObject(pWSCProductList);
        }
    }

    // The projected properties hand back an owned BSTR, so it must be released once it has been copied to a managed string.
    private static unsafe string? GetString(Func<BSTR> getter)
    {
        var value = default(BSTR);
        try
        {
            value = getter();
            return value.ToString();
        }
        catch
        {
            return null;
        }
        finally
        {
            if (value != default)
            {
                Marshal.FreeBSTR(value);
            }
        }
    }

    private static string? GetHealthStatus()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(6, 0, 6000))
            return null;

        WSC_SECURITY_PROVIDER_HEALTH health = default;
        var hr = PInvoke.WscGetSecurityProviderHealth((uint)WSC_SECURITY_PROVIDER.WSC_SECURITY_PROVIDER_ANTIVIRUS, ref health);
        if (hr.Succeeded)
        {
            return health switch
            {
                WSC_SECURITY_PROVIDER_HEALTH.WSC_SECURITY_PROVIDER_HEALTH_GOOD => "Good",
                WSC_SECURITY_PROVIDER_HEALTH.WSC_SECURITY_PROVIDER_HEALTH_NOTMONITORED => "Not monitored",
                WSC_SECURITY_PROVIDER_HEALTH.WSC_SECURITY_PROVIDER_HEALTH_POOR => "Poor",
                WSC_SECURITY_PROVIDER_HEALTH.WSC_SECURITY_PROVIDER_HEALTH_SNOOZE => "Snooze",
                _ => null,
            };
        }

        return null;
    }
}
