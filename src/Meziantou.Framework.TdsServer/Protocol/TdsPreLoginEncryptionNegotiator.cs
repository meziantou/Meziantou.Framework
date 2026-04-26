namespace Meziantou.Framework.Tds.Protocol;

internal static class TdsPreLoginEncryptionNegotiator
{
    public static TdsPreLoginNegotiationResult Negotiate(TdsPreLoginEncryptionMode clientMode, bool serverSupportsEncryption, bool serverRequiresEncryption)
    {
        if (!serverSupportsEncryption)
        {
            return clientMode switch
            {
                TdsPreLoginEncryptionMode.Required => new(TdsPreLoginEncryptionMode.NotSupported, UpgradeToTls: false, DowngradeAfterLogin: false, RejectConnection: true),
                _ => new(TdsPreLoginEncryptionMode.NotSupported, UpgradeToTls: false, DowngradeAfterLogin: false, RejectConnection: false),
            };
        }

        if (serverRequiresEncryption)
        {
            return clientMode switch
            {
                TdsPreLoginEncryptionMode.NotSupported => new(TdsPreLoginEncryptionMode.Required, UpgradeToTls: false, DowngradeAfterLogin: false, RejectConnection: true),
                _ => new(TdsPreLoginEncryptionMode.Required, UpgradeToTls: true, DowngradeAfterLogin: false, RejectConnection: false),
            };
        }

        return clientMode switch
        {
            TdsPreLoginEncryptionMode.NotSupported => new(TdsPreLoginEncryptionMode.NotSupported, UpgradeToTls: false, DowngradeAfterLogin: false, RejectConnection: false),
            TdsPreLoginEncryptionMode.Off => new(TdsPreLoginEncryptionMode.Off, UpgradeToTls: true, DowngradeAfterLogin: true, RejectConnection: false),
            TdsPreLoginEncryptionMode.On => new(TdsPreLoginEncryptionMode.On, UpgradeToTls: true, DowngradeAfterLogin: false, RejectConnection: false),
            TdsPreLoginEncryptionMode.Required => new(TdsPreLoginEncryptionMode.Required, UpgradeToTls: true, DowngradeAfterLogin: false, RejectConnection: false),
            _ => throw new InvalidOperationException("Unsupported PRELOGIN encryption mode."),
        };
    }
}
