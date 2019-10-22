using System;

namespace Meziantou.Framework.Win32.Natives
{
    [Flags]
    internal enum PromptForWindowsCredentialsFlags : uint
    {
        GenericCredentials = 0x1,
        ShowCheckbox = 0x2,
        AuthpackageOnly = 0x10,
        InCredOnly = 0x20,
        EnumerateAdmins = 0x100,
        EnumerateCurrentUser = 0x200,
        SecurePrompt = 0x1000,
        Pack32Wow = 0x10000000,
    }
}
