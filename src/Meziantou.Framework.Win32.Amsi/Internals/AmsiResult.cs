namespace Meziantou.Framework.Win32;

// https://www.magnumdb.com/search?q=parent:AMSI_RESULT
internal enum AmsiResult
{
    AMSI_RESULT_CLEAN = 0,
    AMSI_RESULT_NOT_DETECTED = 1,
    AMSI_RESULT_BLOCKED_BY_ADMIN_START = 16384,
    AMSI_RESULT_BLOCKED_BY_ADMIN_END = 20479,
    AMSI_RESULT_DETECTED = 32768,
}
