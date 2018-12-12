using System;
using System.ComponentModel;

namespace Meziantou.Framework.Win32
{
    public class AmsiApplication : IDisposable
    {
        private readonly AmsiContextSafeHandle _context;

        private AmsiApplication(AmsiContextSafeHandle context)
        {
            _context = context;
        }

        public static AmsiApplication Create(string applicationName)
        {
            int result = Amsi.AmsiInitialize(applicationName, out var context);
            if (result != 0)
                throw new Win32Exception(result);

            return new AmsiApplication(context);
        }

        public AmsiSession CreateSession()
        {
            var result = Amsi.AmsiOpenSession(_context, out var session);
            session.Context = _context;
            if (result != 0)
                throw new Win32Exception(result);

            return new AmsiSession(_context, session);
        }

        public void Dispose()
        {
            _context.Dispose();
        }
    }
}
