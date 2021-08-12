using System.Runtime.InteropServices;

namespace Meziantou.Framework.Win32;

[ComVisible(true)]
public enum DialogResult
{
    None = 0,
    OK = 1,
    Cancel = 2,
    Abort = 3,
    Retry = 4,
    Ignore = 5,
    Yes = 6,
    No = 7,
}
