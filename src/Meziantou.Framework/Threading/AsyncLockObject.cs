using System.Runtime.InteropServices;

namespace Meziantou.Framework.Threading
{
    [StructLayout(LayoutKind.Auto)]
    public readonly struct AsyncLockObject : IDisposable
    {
        private readonly AsyncLock? _parent;

        internal AsyncLockObject(AsyncLock? parent)
        {
            _parent = parent;
        }

        public void Dispose()
        {
            _parent?.Release();
        }
    }
}
