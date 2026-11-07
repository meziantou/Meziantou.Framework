using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Scripting;

// The Device Memory API (https://www.w3.org/TR/device-memory/#sec-device-memory-js-api): webref has no IDL of the
// specification, so the attribute of the NavigatorDeviceMemory mixin is defined by hand
public sealed partial class WebIdlRealm<TAdapter, TValue>
{
    /// <summary>
    /// Defines <c>[SecureContext, Exposed=(Window,Worker)] readonly attribute double deviceMemory</c> on the prototype of
    /// <c>Navigator</c> (in a window) or <c>WorkerNavigator</c> (in a worker).
    /// </summary>
    private void DefineDeviceMemory()
    {
        if (Window is not null)
        {
            DefineAttribute(GetPrototype("Navigator"), "deviceMemory", static (realm, thisValue) => realm.Adapter.FromNumber(realm.ToInterface<Navigator>(thisValue, "Navigator").DeviceMemory), setter: null);
        }
        else
        {
            DefineAttribute(GetPrototype("WorkerNavigator"), "deviceMemory", static (realm, thisValue) => realm.Adapter.FromNumber(realm.ToInterface<WorkerNavigator>(thisValue, "WorkerNavigator").DeviceMemory), setter: null);
        }
    }
}
