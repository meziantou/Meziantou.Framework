using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Scripting;

// The descriptor of navigator.permissions.query() (https://w3c.github.io/permissions/#dom-permissions-query): an object
// converted to PermissionDescriptor, then to the descriptor type of the powerful feature it names
public sealed partial class WebIdlRealm<TAdapter, TValue>
{
    /// <summary>
    /// Converts the argument of <c>query()</c>: steps 2 to 4 of https://w3c.github.io/permissions/#dom-permissions-query.
    /// The object is converted to a <c>PermissionDescriptor</c>, then, when its name is supported, a second time to the
    /// permission descriptor type of the feature (e.g. <c>MidiPermissionDescriptor</c>): the getters of the members are
    /// called twice. A name that is not supported is checked by <see cref="Permissions.Query"/>, after the document.
    /// </summary>
    internal PermissionDescriptor ToPermissionDescriptor(TValue? value)
    {
        value ??= Adapter.Undefined;
        var name = GetPermissionName(value);
        if (!PermissionDescriptor.IsSupportedName(name))
            return new PermissionDescriptor { Name = name };

        // The members of the dictionary are read in order: the inherited name, then the members of the typed descriptor
        GetPermissionName(value);
        var hasUnsupportedOptions = false;
        switch (name)
        {
            case "midi":
                GetBooleanDictionaryMember(value, "sysex");
                break;

            case "push":
                GetBooleanDictionaryMember(value, "userVisibleOnly");
                break;

            case "camera":
                GetBooleanDictionaryMember(value, "panTiltZoom");
                break;

            case "clipboard-read" or "clipboard-write":
                GetBooleanDictionaryMember(value, "allowWithoutGesture");
                break;

            case "fullscreen":
                // https://github.com/explainers-by-googlers/html-fullscreen-without-a-gesture: only the permission to enter
                // fullscreen without a user gesture is a powerful feature
                hasUnsupportedOptions = !GetBooleanDictionaryMember(value, "allowWithoutGesture");
                break;
        }

        return new PermissionDescriptor { Name = name, HasUnsupportedOptions = hasUnsupportedOptions };
    }

    private string GetPermissionName(TValue value)
    {
        CheckDictionary(value, "PermissionDescriptor");
        var name = GetDictionaryMember(value, "name");
        if (Adapter.IsUndefined(name))
            throw CreateTypeError("The required member 'name' of 'PermissionDescriptor' is missing");

        return ToDomString(name, legacyNullToEmptyString: false);
    }

    private bool GetBooleanDictionaryMember(TValue dictionary, string name)
    {
        var value = GetDictionaryMember(dictionary, name);
        return !Adapter.IsUndefined(value) && ToBoolean(value);
    }
}
