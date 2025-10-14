using System.Collections.Immutable;
using System.Runtime.InteropServices;
using Meziantou.Framework.Diagnostics.ContextSnapshot.Internals;

namespace Meziantou.Framework.Diagnostics.ContextSnapshot;

public sealed class SecurityProvidersSnapshot
{
    private static readonly Guid CLSID_WSCProductList = new(0x17072F7B, 0x9ABE, 0x4A74, 0xA2, 0x61, 0x1E, 0xB7, 0x6B, 0x55, 0x10, 0x7A) /* 17072F7B-9ABE-4A74-A261-1EB76B55107A */;

    internal SecurityProvidersSnapshot()
    {
    }

    public string? HealthStatus { get; } = Utils.SafeGet(GetHealthStatus);
    public ImmutableArray<SecurityProviderSnapshot> Antivirus { get; } = Get(WSC_SECURITY_PROVIDER.WSC_SECURITY_PROVIDER_ANTIVIRUS);
    public ImmutableArray<SecurityProviderSnapshot> Firewall { get; } = Get(WSC_SECURITY_PROVIDER.WSC_SECURITY_PROVIDER_FIREWALL);
    public ImmutableArray<SecurityProviderSnapshot> AntiSpyware { get; } = Get(WSC_SECURITY_PROVIDER.WSC_SECURITY_PROVIDER_ANTISPYWARE);

    private static ImmutableArray<SecurityProviderSnapshot> Get(WSC_SECURITY_PROVIDER provider)
    {
        if (!OperatingSystem.IsWindows())
            return ImmutableArray<SecurityProviderSnapshot>.Empty;

        var wscProductListType = Type.GetTypeFromCLSID(CLSID_WSCProductList, throwOnError: false);
        if (wscProductListType == null)
            return ImmutableArray<SecurityProviderSnapshot>.Empty;

        var wscProductList = Utils.SafeGet(() => Activator.CreateInstance(wscProductListType));
        if (wscProductList is null)
            return ImmutableArray<SecurityProviderSnapshot>.Empty;

        var pWSCProductList = (IWSCProductList)wscProductList;
        var hr = pWSCProductList.Initialize((uint)provider);
        if (hr != HRESULT.S_OK)
            return ImmutableArray<SecurityProviderSnapshot>.Empty;

        hr = pWSCProductList.get_Count(out var nProductCount);
        if (hr != HRESULT.S_OK)
            return ImmutableArray<SecurityProviderSnapshot>.Empty;

        var products = ImmutableArray.CreateBuilder<SecurityProviderSnapshot>(initialCapacity: (int)nProductCount);
        for (uint i = 0; i < nProductCount; i++)
        {
            string? productName = null;
            string? productState = null;
            string? productStatus = null;
            string? remediationPath = null;
            string? stateTimestamp = null;

            hr = pWSCProductList.get_Item(i, out var pWscProduct);
            if (hr == HRESULT.S_OK)
            {
                pWscProduct.get_ProductName(out productName);
                hr = pWscProduct.get_ProductState(out var nProductState);
                if (hr == HRESULT.S_OK)
                {
                    productState = nProductState switch
                    {
                        WSC_SECURITY_PRODUCT_STATE.WSC_SECURITY_PRODUCT_STATE_ON => "On",
                        WSC_SECURITY_PRODUCT_STATE.WSC_SECURITY_PRODUCT_STATE_OFF => "Off",
                        WSC_SECURITY_PRODUCT_STATE.WSC_SECURITY_PRODUCT_STATE_SNOOZED => "Snoozed",
                        _ => "Expired",
                    };
                }

                if (provider != WSC_SECURITY_PROVIDER.WSC_SECURITY_PROVIDER_FIREWALL)
                {
                    hr = pWscProduct.get_SignatureStatus(out var nProductStatus);
                    if (hr == HRESULT.S_OK)
                    {
                        productStatus = (nProductStatus == WSC_SECURITY_SIGNATURE_STATUS.WSC_SECURITY_PRODUCT_UP_TO_DATE) ? "Up-to-date" : "Out-of-date";
                    }
                }

                pWscProduct.get_RemediationPath(out remediationPath);
                if (provider == WSC_SECURITY_PROVIDER.WSC_SECURITY_PROVIDER_ANTIVIRUS)
                {
                    pWscProduct.get_ProductStateTimestamp(out stateTimestamp);
                }
            }

            products.Add(new SecurityProviderSnapshot(productName, remediationPath, productStatus, productState, stateTimestamp));

            Marshal.ReleaseComObject(pWSCProductList);
        }

        return products.ToImmutable();
    }

    private static string? GetHealthStatus()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(6, 0, 6000))
            return null;

        Windows.Win32.System.SecurityCenter.WSC_SECURITY_PROVIDER_HEALTH health = default;
        var hr = Windows.Win32.PInvoke.WscGetSecurityProviderHealth((uint)WSC_SECURITY_PROVIDER.WSC_SECURITY_PROVIDER_ANTIVIRUS, ref health);
        if (hr.Succeeded)
        {
            return health switch
            {
                Windows.Win32.System.SecurityCenter.WSC_SECURITY_PROVIDER_HEALTH.WSC_SECURITY_PROVIDER_HEALTH_GOOD => "Good",
                Windows.Win32.System.SecurityCenter.WSC_SECURITY_PROVIDER_HEALTH.WSC_SECURITY_PROVIDER_HEALTH_NOTMONITORED => "Not monitored",
                Windows.Win32.System.SecurityCenter.WSC_SECURITY_PROVIDER_HEALTH.WSC_SECURITY_PROVIDER_HEALTH_POOR => "Poor",
                Windows.Win32.System.SecurityCenter.WSC_SECURITY_PROVIDER_HEALTH.WSC_SECURITY_PROVIDER_HEALTH_SNOOZE => "Snooze",
                _ => null,
            };
        }

        return null;
    }
}

