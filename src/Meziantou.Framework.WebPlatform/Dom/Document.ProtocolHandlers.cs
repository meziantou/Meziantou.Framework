namespace Meziantou.Framework.WebPlatform.Dom;

public partial class Document
{
    /// <summary>
    /// Gets or sets the <c>registerProtocolHandler()</c> automation mode of the document
    /// (https://html.spec.whatwg.org/multipage/system-state.html#registerprotocolhandler()-automation-mode), as the
    /// <c>Set RPH Registration Mode</c> WebDriver extension command: the registrations of <c>navigator.registerProtocolHandler()</c>
    /// are accepted or rejected without asking the user (<see cref="ProtocolHandlerOptions.RequestRegistration"/>). The
    /// default is <see cref="ProtocolHandlerAutomationMode.None"/>.
    /// </summary>
    [NotInIdl]
    public ProtocolHandlerAutomationMode ProtocolHandlerAutomationMode { get; set; }
}
