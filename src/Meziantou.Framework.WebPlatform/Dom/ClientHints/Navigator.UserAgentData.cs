namespace Meziantou.Framework.WebPlatform.Dom;

public sealed partial class Navigator
{
    private NavigatorUAData? _userAgentData;

    /// <summary>
    /// Gets the identity of the user agent given by the User-Agent Client Hints (https://wicg.github.io/ua-client-hints/#dom-navigatorua-useragentdata):
    /// the values of <see cref="BrowsingContextOptions.ClientHints"/>. The scripts only see it in a secure context.
    /// </summary>
    [IdlMember("userAgentData", "https://wicg.github.io/ua-client-hints/#dom-navigatorua-useragentdata")]
    public NavigatorUAData UserAgentData => _userAgentData ??= new NavigatorUAData(_window);
}

public sealed partial class WorkerNavigator
{
    private NavigatorUAData? _userAgentData;

    /// <summary>
    /// Gets the identity of the user agent given by the User-Agent Client Hints (https://wicg.github.io/ua-client-hints/#dom-navigatorua-useragentdata):
    /// the values of <see cref="BrowsingContextOptions.ClientHints"/>. The scripts only see it in a secure context.
    /// </summary>
    [IdlMember("userAgentData", "https://wicg.github.io/ua-client-hints/#dom-navigatorua-useragentdata")]
    public NavigatorUAData UserAgentData => _userAgentData ??= new NavigatorUAData(_global);
}
