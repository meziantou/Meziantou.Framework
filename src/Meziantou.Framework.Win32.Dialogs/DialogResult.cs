using System.Runtime.InteropServices;

namespace Meziantou.Framework.Win32;

/// <summary>Specifies the return value of a dialog box.</summary>
[ComVisible(true)]
public enum DialogResult
{
    /// <summary>The dialog box return value is None (usually sent from a dialog box).</summary>
    None = 0,

    /// <summary>The dialog box return value is OK (usually sent from a button labeled OK).</summary>
    OK = 1,

    /// <summary>The dialog box return value is Cancel (usually sent from a button labeled Cancel).</summary>
    Cancel = 2,

    /// <summary>The dialog box return value is Abort (usually sent from a button labeled Abort).</summary>
    Abort = 3,

    /// <summary>The dialog box return value is Retry (usually sent from a button labeled Retry).</summary>
    Retry = 4,

    /// <summary>The dialog box return value is Ignore (usually sent from a button labeled Ignore).</summary>
    Ignore = 5,

    /// <summary>The dialog box return value is Yes (usually sent from a button labeled Yes).</summary>
    Yes = 6,

    /// <summary>The dialog box return value is No (usually sent from a button labeled No).</summary>
    No = 7,
}
