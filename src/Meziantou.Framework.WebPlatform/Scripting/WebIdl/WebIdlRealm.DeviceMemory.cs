using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Scripting;

// The Device Memory API (https://www.w3.org/TR/device-memory/#sec-device-memory-js-api): webref has no IDL of the
// specification, so the attribute of the NavigatorDeviceMemory mixin is defined by hand
public sealed partial class WebIdlRealm<TAdapter, TValue>
{
    /// <summary>
    /// The interface whose prototype gets the <c>deviceMemory</c> attribute when it is created (see
    /// <see cref="CreateInterfaceObject"/>), or null when the realm does not expose it.
    /// </summary>
    private string? _deviceMemoryInterface;

    /// <summary>
    /// Prepares <c>[SecureContext, Exposed=(Window,Worker)] readonly attribute double deviceMemory</c>, defined on the prototype
    /// of <c>Navigator</c> (in a window) or <c>WorkerNavigator</c> (in a worker) after its members, when the prototype is created:
    /// the prototype is not created with the realm.
    /// </summary>
    private void InitializeDeviceMemory(bool exposed)
    {
        if (!exposed)
            return;

        var interfaceName = Window is not null ? "Navigator" : "WorkerNavigator";
        if (_interfaceObjects.TryGetValue(interfaceName, out var existing))
        {
            DefineDeviceMemory(existing.Prototype);
            return;
        }

        _deviceMemoryInterface = interfaceName;
    }

    private void DefineDeviceMemory(TValue prototype)
    {
        // (as a member defined with the realm, not as a member of the interface)
        var previousDefiningInterface = _definingInterface;
        _definingInterface = null;
        try
        {
            if (Window is not null)
            {
                DefineAttribute(prototype, "deviceMemory", static (realm, thisValue) => realm.Adapter.FromNumber(realm.ToInterface<Navigator>(thisValue, "Navigator").DeviceMemory), setter: null);
            }
            else
            {
                DefineAttribute(prototype, "deviceMemory", static (realm, thisValue) => realm.Adapter.FromNumber(realm.ToInterface<WorkerNavigator>(thisValue, "WorkerNavigator").DeviceMemory), setter: null);
            }
        }
        finally
        {
            _definingInterface = previousDefiningInterface;
        }
    }
}
