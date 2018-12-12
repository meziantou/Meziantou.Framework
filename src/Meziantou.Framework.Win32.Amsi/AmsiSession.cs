using System;
using System.ComponentModel;

namespace Meziantou.Framework.Win32
{
    public class AmsiSession : IDisposable
    {
        private readonly AmsiContextSafeHandle _context;
        private readonly AmsiSessionSafeHandle _session;

        internal AmsiSession(AmsiContextSafeHandle context, AmsiSessionSafeHandle session)
        {
            _context = context;
            _session = session;
        }

        public bool IsMalware(string payload, string contentName)
        {
            var returnValue = Amsi.AmsiScanString(_context, payload, contentName, _session, out var result);
            if (returnValue != 0)
                throw new Win32Exception(returnValue);

            return Amsi.AmsiResultIsMalware(result);
        }

        public bool IsMalware(byte[] payload, string contentName)
        {
            var returnValue = Amsi.AmsiScanBuffer(_context, payload, (uint)payload.Length, contentName, _session, out var result);
            if (returnValue != 0)
                throw new Win32Exception(returnValue);

            return Amsi.AmsiResultIsMalware(result);
        }

        public void Dispose()
        {
            _session.Dispose();
            _context.Dispose();
        }
    }
}
