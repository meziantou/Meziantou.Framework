namespace Meziantou.Framework.Diagnostics.ContextSnapshot.Internals;

internal enum WSC_SECURITY_PROVIDER : int
{
    // Represents the aggregation of all firewalls for this computer.
    WSC_SECURITY_PROVIDER_FIREWALL = 0x1,
    // Represents the Automatic updating settings for this computer.
    WSC_SECURITY_PROVIDER_AUTOUPDATE_SETTINGS = 0x2,
    // Represents the aggregation of all antivirus products for this computer.
    WSC_SECURITY_PROVIDER_ANTIVIRUS = 0x4,
    // Represents the aggregation of all antispyware products for this computer.
    WSC_SECURITY_PROVIDER_ANTISPYWARE = 0x8,
    // Represents the settings that restrict the access of web sites in each of the internet zones.
    WSC_SECURITY_PROVIDER_INTERNET_SETTINGS = 0x10,
    // Represents the User Account Control settings on this machine.
    WSC_SECURITY_PROVIDER_USER_ACCOUNT_CONTROL = 0x20,
    // Represents the running state of the Security Center service on this machine.
    WSC_SECURITY_PROVIDER_SERVICE = 0x40,

    WSC_SECURITY_PROVIDER_NONE = 0,

    // Aggregates all of the items that Security Center monitors.
    WSC_SECURITY_PROVIDER_ALL = WSC_SECURITY_PROVIDER_FIREWALL |
                                                            WSC_SECURITY_PROVIDER_AUTOUPDATE_SETTINGS |
                                                            WSC_SECURITY_PROVIDER_ANTIVIRUS |
                                                            WSC_SECURITY_PROVIDER_ANTISPYWARE |
                                                            WSC_SECURITY_PROVIDER_INTERNET_SETTINGS |
                                                            WSC_SECURITY_PROVIDER_USER_ACCOUNT_CONTROL |
                                                            WSC_SECURITY_PROVIDER_SERVICE,
}
