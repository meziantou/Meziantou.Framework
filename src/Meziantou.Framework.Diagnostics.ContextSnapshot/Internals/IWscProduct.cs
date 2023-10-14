#pragma warning disable IDE1006 // Naming Styles
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Diagnostics.ContextSnapshot.Internals;

[ComImport]
[Guid("8C38232E-3A45-4A27-92B0-1A16A975F669")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IWscProduct
{
    int GetTypeInfoCount();
    [return: MarshalAs(UnmanagedType.Interface)]
    IntPtr GetTypeInfo([In, MarshalAs(UnmanagedType.U4)] int iTInfo, [In, MarshalAs(UnmanagedType.U4)] int lcid);
    [PreserveSig]
    HRESULT GetIDsOfNames([In] ref Guid riid, [In, MarshalAs(UnmanagedType.LPArray)] string[] rgszNames, [In, MarshalAs(UnmanagedType.U4)] int cNames,
        [In, MarshalAs(UnmanagedType.U4)] int lcid, [Out, MarshalAs(UnmanagedType.LPArray)] int[] rgDispId);
    [PreserveSig]
    HRESULT Invoke(int dispIdMember, [In] ref Guid riid, [In, MarshalAs(UnmanagedType.U4)] int lcid, [In, MarshalAs(UnmanagedType.U4)] int dwFlags,
        [Out, In] System.Runtime.InteropServices.ComTypes.DISPPARAMS pDispParams, [Out] out object pVarResult, [Out, In] System.Runtime.InteropServices.ComTypes.EXCEPINFO pExcepInfo, [Out, MarshalAs(UnmanagedType.LPArray)] IntPtr[] pArgErr);

    HRESULT get_ProductName(out string pVal);
    HRESULT get_ProductState(out WSC_SECURITY_PRODUCT_STATE pVal);
    HRESULT get_SignatureStatus(out WSC_SECURITY_SIGNATURE_STATUS pVal);
    HRESULT get_RemediationPath(out string pVal);
    HRESULT get_ProductStateTimestamp(out string pVal);
}
