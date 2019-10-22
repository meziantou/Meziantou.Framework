using System;
using Meziantou.Framework.Win32.Natives;

namespace Meziantou.Framework.Win32
{
    public class OpenFolderDialog
    {
        public DialogResult ShowDialog()
        {
            return ShowDialog(IntPtr.Zero);
        }

        public DialogResult ShowDialog(IntPtr owner) // IWin32Window
        {
            var hwndOwner = owner != IntPtr.Zero ? owner : NativeMethods.GetActiveWindow();
            var dialog = (IFileOpenDialog)new NativeFileOpenDialog();
            Configure(dialog);

            var hr = dialog.Show(hwndOwner);
            if (hr == NativeMethods.ERROR_CANCELLED)
                return DialogResult.Cancel;

            if (hr != NativeMethods.S_OK)
                return DialogResult.Abort;

            dialog.GetResult(out var item);
            item.GetDisplayName(SIGDN.SIGDN_FILESYSPATH, out var path);
            SelectedPath = path;
            return DialogResult.OK;
        }

        public string? Title { get; set; }
        public string? OkButtonLabel { get; set; }
        public string? InitialDirectory { get; set; }
        public string? SelectedPath { get; set; }
        public bool ChangeCurrentDirectory { get; set; }

        private void Configure(IFileOpenDialog dialog)
        {
            dialog.SetOptions(CreateOptions());

            if (!string.IsNullOrEmpty(InitialDirectory))
            {
                NativeMethods.SHCreateItemFromParsingName(InitialDirectory, IntPtr.Zero, typeof(IShellItem).GUID, out var item);
                if (item != null)
                {
                    dialog.SetFolder(item);
                }
            }

            if (Title != null)
            {
                dialog.SetTitle(Title);
            }

            if (OkButtonLabel != null)
            {
                dialog.SetOkButtonLabel(OkButtonLabel);
            }
        }

        private FOS CreateOptions()
        {
            var result = FOS.FOS_FORCEFILESYSTEM | FOS.FOS_PICKFOLDERS;
            if (!ChangeCurrentDirectory)
            {
                result |= FOS.FOS_NOCHANGEDIR;
            }

            return result;
        }
    }
}
