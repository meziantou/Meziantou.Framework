using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Scripting.Generated;

namespace Meziantou.Framework.WebPlatform.Scripting;

// The data of an NDEF record (NDEFRecordInit.data) is an any value, which the record converts itself depending on its type
public sealed partial class WebIdlRealm<TAdapter, TValue>
{
    bool IScriptValueOperations.IsString(object? value) => TryGetScriptValue(value, out var jsValue) ? Adapter.IsString(jsValue) : DotNetScriptValueOperations.Instance.IsString(value);

    byte[]? IScriptValueOperations.GetBufferSourceCopy(object? value)
    {
        if (!TryGetScriptValue(value, out var jsValue))
            return DotNetScriptValueOperations.Instance.GetBufferSourceCopy(value);

        return TryToBufferSource(jsValue, IdlBufferSourceKinds, allowShared: false)?.ToArray();
    }

    NdefMessageInit IScriptValueOperations.ToNdefMessageInit(object? value)
    {
        if (value is NdefMessageInit messageInit)
            return messageInit;

        if (!TryGetScriptValue(value, out var jsValue))
            return DotNetScriptValueOperations.Instance.ToNdefMessageInit(value);

        return WebIdlHelpers<TAdapter, TValue>.ToDictionary_Dom_NdefMessageInit(this, jsValue);
    }
}
