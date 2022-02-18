using System.Runtime.InteropServices;

namespace Meziantou.Framework.Win32.Natives
{
    [ComImport(),
    Guid(IIDGuid.IFileSaveDialog),
    InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IFileSaveDialog : IFileDialog
    {
        [PreserveSig]
        new int Show([In] IntPtr parent);

        void SetFileTypes([In] uint cFileTypes, [In] ref COMDLG_FILTERSPEC rgFilterSpec);
        new void SetFileTypeIndex([In] uint iFileType);
        new void GetFileTypeIndex(out uint piFileType);
        new void Advise([In, MarshalAs(UnmanagedType.Interface)] IFileDialogEvents pfde, out uint pdwCookie);
        new void Unadvise([In] uint dwCookie);
        new void SetOptions([In] FOS fos);
        new void GetOptions(out FOS pfos);
        new void SetDefaultFolder([In, MarshalAs(UnmanagedType.Interface)] IShellItem psi);
        new void SetFolder([In, MarshalAs(UnmanagedType.Interface)] IShellItem psi);
        new void GetFolder([MarshalAs(UnmanagedType.Interface)] out IShellItem ppsi);
        new void GetCurrentSelection([MarshalAs(UnmanagedType.Interface)] out IShellItem ppsi);
        new void SetFileName([In, MarshalAs(UnmanagedType.LPWStr)] string pszName);
        new void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
        new void SetTitle([In, MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
        new void SetOkButtonLabel([In, MarshalAs(UnmanagedType.LPWStr)] string pszText);
        new void SetFileNameLabel([In, MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
        new void GetResult([MarshalAs(UnmanagedType.Interface)] out IShellItem ppsi);
        //void AddPlace([In, MarshalAs(UnmanagedType.Interface)] IShellItem psi, FileDialogCustomPlace fdcp);
        void AddPlace(); // incomplete signature
        new void SetDefaultExtension([In, MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
        new void Close([MarshalAs(UnmanagedType.Error)] int hr);
        new void SetClientGuid([In] ref Guid guid);
        new void ClearClientData();
        new void SetFilter([MarshalAs(UnmanagedType.Interface)] IntPtr pFilter);
        void SetSaveAsItem([In, MarshalAs(UnmanagedType.Interface)] IShellItem psi);
        void SetProperties([In, MarshalAs(UnmanagedType.Interface)] IntPtr pStore);
        void SetCollectedProperties([In, MarshalAs(UnmanagedType.Interface)] IntPtr pList, [In] int fAppendDefault);
        void GetProperties([MarshalAs(UnmanagedType.Interface)] out IntPtr ppStore);
        void ApplyProperties([In, MarshalAs(UnmanagedType.Interface)] IShellItem psi, [In, MarshalAs(UnmanagedType.Interface)] IntPtr pStore, [In, ComAliasName("ShellObjects.wireHWND")] ref IntPtr hwnd, [In, MarshalAs(UnmanagedType.Interface)] IntPtr pSink);
    }
}
