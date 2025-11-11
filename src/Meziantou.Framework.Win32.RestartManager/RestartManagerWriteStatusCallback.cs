namespace Meziantou.Framework.Win32;

/// <summary>Represents the callback method that receives progress updates during Restart Manager shutdown and restart operations.</summary>
/// <param name="percentComplete">The percentage of the operation that has been completed (0-100).</param>
public delegate void RestartManagerWriteStatusCallback(uint percentComplete);
