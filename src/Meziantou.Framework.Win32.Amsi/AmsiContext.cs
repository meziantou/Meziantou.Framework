using System.ComponentModel;
using System.Runtime.Versioning;

namespace Meziantou.Framework.Win32;

[SupportedOSPlatform("windows")]
public sealed class AmsiContext : IDisposable
{
    internal readonly AmsiContextSafeHandle _handle;

    private static readonly AmsiSessionSafeHandle DefaultSession = new();

    private AmsiContext(AmsiContextSafeHandle context)
    {
        _handle = context;
    }

    public static AmsiContext Create(string applicationName)
    {
        var result = Amsi.AmsiInitialize(applicationName, out var context);
        if (result != 0)
            throw new Win32Exception(result);

        return new AmsiContext(context);
    }

    public AmsiSession CreateSession()
    {
        var result = Amsi.AmsiOpenSession(_handle, out var session);
        session.Context = _handle;
        if (result != 0)
            throw new Win32Exception(result);

        return new AmsiSession(this, session);
    }

    public bool IsMalware(string payload, string contentName)
    {
        var returnValue = Amsi.AmsiScanString(_handle, payload, contentName, DefaultSession, out var result);
        if (returnValue != 0)
            throw new Win32Exception(returnValue);

        return Amsi.AmsiResultIsMalware(result);
    }

    public bool IsMalware(byte[] payload, string contentName)
    {
        var returnValue = Amsi.AmsiScanBuffer(_handle, payload, (uint)payload.Length, contentName, DefaultSession, out var result);
        if (returnValue != 0)
            throw new Win32Exception(returnValue);

        return Amsi.AmsiResultIsMalware(result);
    }

    public void Dispose()
    {
        _handle.Dispose();
    }
}
