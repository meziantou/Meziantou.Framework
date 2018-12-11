using System;
using System.ComponentModel;

namespace Meziantou.Framework.Win32
{
    public class AmsiSession : IDisposable
    {
        private readonly AmsiContextSafeHandle _context;
        private readonly AmsiSessionSafeHandle _session;

        private AmsiSession(AmsiContextSafeHandle context, AmsiSessionSafeHandle session)
        {
            _context = context;
            _session = session;
        }

        public static AmsiSession Create(string name)
        {
            int result = Amsi.AmsiInitialize(name, out var context);
            if (result != 0)
                throw new Win32Exception(result, "Cannot initialize AMSI");

            result = Amsi.AmsiOpenSession(context, out var session);
            session.Context = context;
            if (result != 0)
            {
                try
                {
                    throw new Win32Exception(result, "Cannot initialize AMSI session");
                }
                finally
                {
                    session.Dispose();
                    context.Dispose();
                }
            }

            return new AmsiSession(context, session);
        }

        public bool IsMalware(string payload, string contentName)
        {
            var returnValue = Amsi.AmsiScanString(_context, payload, contentName, _session, out var result);
            if (returnValue != 0)
                throw new Win32Exception(returnValue, "Cannot scan the string");

            return Amsi.AmsiResultIsMalware(result);
        }

        public bool IsMalware(byte[] payload, string contentName)
        {
            var returnValue = Amsi.AmsiScanBuffer(_context, payload, (uint)payload.Length, contentName, _session, out var result);
            if (returnValue != 0)
                throw new Win32Exception(returnValue, "Cannot scan the buffer");

            return Amsi.AmsiResultIsMalware(result);
        }

        public void Dispose()
        {
            _session.Dispose();
            _context.Dispose();
        }
    }
}
