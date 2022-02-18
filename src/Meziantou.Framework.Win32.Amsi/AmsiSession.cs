using System.ComponentModel;
using System.Runtime.Versioning;

namespace Meziantou.Framework.Win32;

[SupportedOSPlatform("windows")]
public sealed class AmsiSession : IDisposable
{
    private readonly AmsiContext _context;
    private readonly AmsiSessionSafeHandle _sessionHandle;

    internal AmsiSession(AmsiContext context, AmsiSessionSafeHandle session)
    {
        _context = context;
        _sessionHandle = session;
    }

    public bool IsMalware(string payload, string contentName)
    {
        var returnValue = Amsi.AmsiScanString(_context._handle, payload, contentName, _sessionHandle, out var result);
        if (returnValue != 0)
            throw new Win32Exception(returnValue);

        return Amsi.AmsiResultIsMalware(result);
    }

    public bool IsMalware(byte[] payload, string contentName)
    {
        var returnValue = Amsi.AmsiScanBuffer(_context._handle, payload, (uint)payload.Length, contentName, _sessionHandle, out var result);
        if (returnValue != 0)
            throw new Win32Exception(returnValue);

        return Amsi.AmsiResultIsMalware(result);
    }

    public void Dispose()
    {
        _sessionHandle.Dispose();
    }
}
