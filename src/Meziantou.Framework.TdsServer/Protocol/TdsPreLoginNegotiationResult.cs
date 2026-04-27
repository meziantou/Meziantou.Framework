namespace Meziantou.Framework.Tds.Protocol;

internal readonly record struct TdsPreLoginNegotiationResult(
    TdsPreLoginEncryptionMode ResponseEncryptionMode,
    bool UpgradeToTls,
    bool DowngradeAfterLogin,
    bool RejectConnection);
