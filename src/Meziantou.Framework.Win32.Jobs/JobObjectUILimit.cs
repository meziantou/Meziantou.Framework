namespace Meziantou.Framework.Win32.Natives;

/// <summary>Defines UI restrictions for processes in a job object.</summary>
[Flags]
public enum JobObjectUILimit
{
    /// <summary>
    /// Prevents processes associated with the job from using USER handles owned by processes not associated with the same job.
    /// </summary>
    Handles = 0x00000001,

    /// <summary>
    /// Prevents processes associated with the job from reading data from the clipboard.
    /// </summary>
    ReadClipboard = 0x00000002,

    /// <summary>
    /// Prevents processes associated with the job from writing data to the clipboard.
    /// </summary>
    WriteClipboard = 0x00000004,

    /// <summary>
    /// Prevents processes associated with the job from changing system parameters by using the SystemParametersInfo function.
    /// </summary>
    SystemParameters = 0x00000008,

    /// <summary>
    /// Prevents processes associated with the job from calling the ChangeDisplaySettings function.
    /// </summary>
    DisplaySettings = 0x00000010,

    /// <summary>
    /// Prevents processes associated with the job from accessing global atoms. When this flag is used, each job has its own atom table.
    /// </summary>
    GlobalAtoms = 0x00000020,

    /// <summary>
    /// Prevents processes associated with the job from creating desktops and switching desktops using the CreateDesktop and SwitchDesktop functions.
    /// </summary>
    Desktop = 0x00000040,
    /// <summary>
    /// Prevents processes associated with the job from calling the ExitWindows or ExitWindowsEx function.
    /// </summary>
    ExitWindows = 0x00000080,

}
