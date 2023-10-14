#pragma warning disable IDE1006 // Naming Styles
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Diagnostics.ContextSnapshot.Internals;

[ComImport]
[Guid("722A338C-6E8E-4E72-AC27-1417FB0C81C2")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IWSCProductList
{
    int GetTypeInfoCount();
    [return: MarshalAs(UnmanagedType.Interface)]
    IntPtr GetTypeInfo([In, MarshalAs(UnmanagedType.U4)] int iTInfo, [In, MarshalAs(UnmanagedType.U4)] int lcid);

    [PreserveSig]
    HRESULT GetIDsOfNames([In] ref Guid riid, [In, MarshalAs(UnmanagedType.LPArray)] string[] rgszNames, [In, MarshalAs(UnmanagedType.U4)] int cNames, [In, MarshalAs(UnmanagedType.U4)] int lcid, [Out, MarshalAs(UnmanagedType.LPArray)] int[] rgDispId);

    [PreserveSig]
    HRESULT Invoke(int dispIdMember, [In] ref Guid riid, [In, MarshalAs(UnmanagedType.U4)] int lcid, [In, MarshalAs(UnmanagedType.U4)] int dwFlags, [Out, In] System.Runtime.InteropServices.ComTypes.DISPPARAMS pDispParams, [Out] out object pVarResult, [Out, In] System.Runtime.InteropServices.ComTypes.EXCEPINFO pExcepInfo, [Out, MarshalAs(UnmanagedType.LPArray)] IntPtr[] pArgErr);

    HRESULT Initialize(uint provider);
    HRESULT get_Count(out uint pVal);
    HRESULT get_Item(uint index, out IWscProduct pVal);
}