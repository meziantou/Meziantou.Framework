namespace Meziantou.Framework.WebPlatform.Scripting;

/// <summary>The operation of the HostPromiseRejectionTracker abstract operation (https://tc39.es/ecma262/#sec-host-promise-rejection-tracker).</summary>
public enum PromiseRejectionOperation
{
    /// <summary>A promise is rejected without any handler.</summary>
    Reject,

    /// <summary>A handler is added to a rejected promise for the first time.</summary>
    Handle,
}
