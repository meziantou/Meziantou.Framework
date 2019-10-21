#nullable disable
using System;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Win32.Natives
{
    [ComImport()]
    [Guid(IIDGuid.IFileDialog)]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IFileDialog
    {
        [PreserveSig]
        int Show([In] IntPtr parent);

        void SetFileTypes([In] uint cFileTypes, [In] [MarshalAs(UnmanagedType.LPArray)]COMDLG_FILTERSPEC[] rgFilterSpec);

        void SetFileTypeIndex([In] uint iFileType);

        void GetFileTypeIndex(out uint piFileType);

        void Advise([In, MarshalAs(UnmanagedType.Interface)] IFileDialogEvents pfde, out uint pdwCookie);

        void Unadvise([In] uint dwCookie);

        void SetOptions([In] FOS fos);

        void GetOptions(out FOS pfos);

        void SetDefaultFolder([In, MarshalAs(UnmanagedType.Interface)] IShellItem psi);

        void SetFolder([In, MarshalAs(UnmanagedType.Interface)] IShellItem psi);

        void GetFolder([MarshalAs(UnmanagedType.Interface)] out IShellItem ppsi);

        void GetCurrentSelection([MarshalAs(UnmanagedType.Interface)] out IShellItem ppsi);

        void SetFileName([In, MarshalAs(UnmanagedType.LPWStr)] string pszName);

        void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);

        void SetTitle([In, MarshalAs(UnmanagedType.LPWStr)] string pszTitle);

        void SetOkButtonLabel([In, MarshalAs(UnmanagedType.LPWStr)] string pszText);

        void SetFileNameLabel([In, MarshalAs(UnmanagedType.LPWStr)] string pszLabel);

        void GetResult([MarshalAs(UnmanagedType.Interface)] out IShellItem ppsi);

        void AddPlace([In, MarshalAs(UnmanagedType.Interface)] IShellItem psi, int alignment);

        void SetDefaultExtension([In, MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);

        void Close([MarshalAs(UnmanagedType.Error)] int hr);

        void SetClientGuid([In] ref Guid guid);

        void ClearClientData();

        void SetFilter([MarshalAs(UnmanagedType.Interface)] IntPtr pFilter);
    }
}
