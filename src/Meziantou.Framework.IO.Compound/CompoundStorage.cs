using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.IO.Compound
{
    // see http://msdn.microsoft.com/en-us/library/windows/desktop/aa380062(v=vs.85).aspx
    /// <summary>
    /// A utility class to read and write Compound File properties. Also supports NTFS native implementation.
    /// </summary>
    public sealed class CompoundStorage
    {
        private const uint STG_E_FILENOTFOUND = 0x80030002;
        private const uint STG_E_PATHNOTFOUND = 0x80030003;
        private const uint STG_E_ACCESSDENIED = 0x80030005;

        private const uint USERDEFINEDPROPERTIES_MIN = 100; // arbitrary
        private const uint PROPSETFLAG_ANSI = 2;

        private enum PRSPEC
        {
            PRSPEC_LPWSTR = 0,
            PRSPEC_PROPID = 1,
        }

        private enum STGFMT
        {
            STGFMT_ANY = 4,
        }

        [Flags]
        private enum STGM
        {
            STGM_READ = 0x00000000,
            STGM_READWRITE = 0x00000002,
            STGM_SHARE_DENY_NONE = 0x00000040,
            STGM_SHARE_DENY_WRITE = 0x00000020,
            STGM_SHARE_EXCLUSIVE = 0x00000010,
            STGM_DIRECT_SWMR = 0x00400000,
        }

        // we only define what we handle
        private enum VARTYPE : short
        {
            VT_I2 = 2,
            VT_I4 = 3,
            VT_R4 = 4,
            VT_R8 = 5,
            VT_CY = 6,
            VT_DATE = 7,
            VT_BSTR = 8,
            VT_DISPATCH = 9,
            VT_ERROR = 10,
            VT_BOOL = 11,
            VT_UNKNOWN = 13,
            VT_DECIMAL = 14,
            VT_I1 = 16,
            VT_UI1 = 17,
            VT_UI2 = 18,
            VT_UI4 = 19,
            VT_I8 = 20,
            VT_UI8 = 21,
            VT_INT = 22,
            VT_UINT = 23,
            VT_HRESULT = 25,
            VT_LPSTR = 30,
            VT_LPWSTR = 31,
            VT_FILETIME = 64,
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct PROPVARIANTunion
        {
            [FieldOffset(0)]
            public sbyte cVal;
            [FieldOffset(0)]
            public byte bVal;
            [FieldOffset(0)]
            public short iVal;
            [FieldOffset(0)]
            public ushort uiVal;
            [FieldOffset(0)]
            public int lVal;
            [FieldOffset(0)]
            public uint ulVal;
            [FieldOffset(0)]
            public int intVal;
            [FieldOffset(0)]
            public uint uintVal;
            [FieldOffset(0)]
            public long hVal;
            [FieldOffset(0)]
            public ulong uhVal;
            [FieldOffset(0)]
            public float fltVal;
            [FieldOffset(0)]
            public double dblVal;
            [FieldOffset(0)]
            public short boolVal;
            [FieldOffset(0)]
            public int scode;
            [FieldOffset(0)]
            public long cyVal;
            [FieldOffset(0)]
            public double date;
            [FieldOffset(0)]
            public long filetime;
            [FieldOffset(0)]
            public IntPtr bstrVal;
            [FieldOffset(0)]
            public IntPtr pszVal;
            [FieldOffset(0)]
            public IntPtr pwszVal;
            [FieldOffset(0)]
            public IntPtr punkVal;
            [FieldOffset(0)]
            public IntPtr pdispVal;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PROPSPEC
        {
            public PRSPEC ulKind;
            public PROPSPECunion union;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct PROPSPECunion
        {
            [FieldOffset(0)]
            public uint propid;
            [FieldOffset(0)]
            public IntPtr lpwstr;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PROPVARIANT
        {
            public VARTYPE vt;
            public ushort wReserved1;
            public ushort wReserved2;
            public ushort wReserved3;
            public PROPVARIANTunion union;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct STATPROPSTG
        {
            [MarshalAs(UnmanagedType.LPWStr)]
            public string lpwstrName;
            public uint propid;
            public VARTYPE vt;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct STATPROPSETSTG
        {
            public Guid fmtid;
            public Guid clsid;
            public uint grfFlags;
            public System.Runtime.InteropServices.ComTypes.FILETIME mtime;
            public System.Runtime.InteropServices.ComTypes.FILETIME ctime;
            public System.Runtime.InteropServices.ComTypes.FILETIME atime;
            public uint dwOSVersion;
        }

        [DllImport("ole32.dll")]
        private static extern uint StgOpenStorageEx([In, MarshalAs(UnmanagedType.LPWStr)] string pwcsName, STGM grfMode, STGFMT stgfmt, int grfAttrs, IntPtr pStgOptions, IntPtr reserved2, ref Guid riid, out IPropertySetStorage ppObjectOpen);

        [DllImport("ole32.dll")]
        private static extern int PropVariantClear(ref PROPVARIANT pvar);

        [ComImport]
        [Guid("0000013B-0000-0000-C000-000000000046")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IEnumSTATPROPSETSTG
        {
            [PreserveSig]
            uint Next(int celt, ref STATPROPSETSTG rgelt, out int pceltFetched);
            // rest ommited
        }

        [ComImport]
        [Guid("00000139-0000-0000-C000-000000000046")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IEnumSTATPROPSTG
        {
            [PreserveSig]
            uint Next(int celt, ref STATPROPSTG rgelt, out int pceltFetched);
            // rest ommited
        }

        [ComImport]
        [Guid("00000138-0000-0000-C000-000000000046")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPropertyStorage
        {
            [PreserveSig]
            uint ReadMultiple(uint cpspec, [In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] PROPSPEC[] rgpspec, [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] PROPVARIANT[] rgpropvar);
            [PreserveSig]
            uint WriteMultiple(uint cpspec, [In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)]  PROPSPEC[] rgpspec, [In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)]  PROPVARIANT[] rgpropvar, uint propidNameFirst);
            [PreserveSig]
            uint DeleteMultiple(uint cpspec, [In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] PROPSPEC[] rgpspec);
            [PreserveSig]
            uint ReadPropertyNames(uint cpropid, [In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] uint[] rgpropid, [Out, MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr, SizeParamIndex = 0)] string?[] rglpwstrName);
            [PreserveSig]
            uint NotDeclared1();
            [PreserveSig]
            uint NotDeclared2();
            [PreserveSig]
            uint Commit(uint grfCommitFlags);
            [PreserveSig]
            uint NotDeclared3();
            [PreserveSig]
            uint Enum(out IEnumSTATPROPSTG ppenum);
            // rest ommited
        }

        [ComImport]
        [Guid("0000013A-0000-0000-C000-000000000046")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPropertySetStorage
        {
            [PreserveSig]
            uint Create(ref Guid rfmtid, ref Guid pclsid, uint grfFlags, STGM grfMode, out IPropertyStorage ppprstg);
            [PreserveSig]
            uint Open(ref Guid rfmtid, STGM grfMode, out IPropertyStorage ppprstg);
            [PreserveSig]
            uint NotDeclared3();
            [PreserveSig]
            uint Enum(out IEnumSTATPROPSETSTG ppenum);
        }

        private static string? GetPropertyName(Guid fmtid, IPropertyStorage propertyStorage, STATPROPSTG stg)
        {
            if (!string.IsNullOrEmpty(stg.lpwstrName))
                return stg.lpwstrName;

            var propids = new uint[1];
            propids[0] = stg.propid;
            var names = new string?[1];
            names[0] = null;
            var hr = propertyStorage.ReadPropertyNames(1, propids, names);
            if (hr == 0)
                return names[0];

            foreach (var kp in CompoundProperty.KnownProperties)
            {
                if (kp.FormatId == fmtid && kp.Id == stg.propid)
                    return kp.Name;
            }
            return null;
        }

        private void LoadPropertySet(IPropertySetStorage propertySetStorage, Guid fmtid)
        {
            var guid = fmtid;
            var hr = propertySetStorage.Open(ref guid, STGM.STGM_READ | STGM.STGM_SHARE_EXCLUSIVE, out var propertyStorage);
            if (hr == STG_E_FILENOTFOUND || hr == STG_E_ACCESSDENIED)
                return;

            if (hr != 0)
                throw new Win32Exception((int)hr);

            propertyStorage.Enum(out var es);
            if (es == null)
                return;

            try
            {
                var stg = new STATPROPSTG();
                int fetched;
                do
                {
                    hr = es.Next(1, ref stg, out fetched);
                    if (hr != 0 && hr != 1)
                        throw new Win32Exception((int)hr);

                    if (fetched == 1)
                    {
                        var name = GetPropertyName(fmtid, propertyStorage, stg);

                        var propsec = new PROPSPEC[]
                        {
                            new PROPSPEC
                            {
                                ulKind = stg.lpwstrName != null ? PRSPEC.PRSPEC_LPWSTR : PRSPEC.PRSPEC_PROPID,
                            },
                        };

                        var lpwstr = IntPtr.Zero;
                        if (stg.lpwstrName != null)
                        {
                            lpwstr = Marshal.StringToCoTaskMemUni(stg.lpwstrName);
                            propsec[0].union.lpwstr = lpwstr;
                        }
                        else
                        {
                            propsec[0].union.propid = stg.propid;
                        }

                        var vars = new PROPVARIANT[1];
                        vars[0] = new PROPVARIANT();
                        try
                        {
                            hr = propertyStorage.ReadMultiple(1, propsec, vars);
                            if (hr != 0)
                                throw new Win32Exception((int)hr);

                        }
                        finally
                        {
                            if (lpwstr != IntPtr.Zero)
                                Marshal.FreeCoTaskMem(lpwstr);
                        }

                        object? value;
                        try
                        {
                            switch (vars[0].vt)
                            {
                                case VARTYPE.VT_BOOL:
                                    value = vars[0].union.boolVal != 0 ? true : false;
                                    break;

                                case VARTYPE.VT_BSTR:
                                    value = Marshal.PtrToStringUni(vars[0].union.bstrVal);
                                    break;

                                case VARTYPE.VT_CY:
                                    value = decimal.FromOACurrency(vars[0].union.cyVal);
                                    break;

                                case VARTYPE.VT_DATE:
                                    value = DateTime.FromOADate(vars[0].union.date);
                                    break;

                                case VARTYPE.VT_DECIMAL:
                                    var dec = IntPtr.Zero;
                                    Marshal.StructureToPtr(vars[0], dec, fDeleteOld: false);
                                    value = Marshal.PtrToStructure(dec, typeof(decimal));
                                    break;

                                case VARTYPE.VT_DISPATCH:
                                    value = Marshal.GetObjectForIUnknown(vars[0].union.pdispVal);
                                    break;

                                case VARTYPE.VT_ERROR:
                                case VARTYPE.VT_HRESULT:
                                    value = vars[0].union.scode;
                                    break;

                                case VARTYPE.VT_FILETIME:
                                    value = DateTime.FromFileTime(vars[0].union.filetime);
                                    break;

                                case VARTYPE.VT_I1:
                                    value = vars[0].union.cVal;
                                    break;

                                case VARTYPE.VT_I2:
                                    value = vars[0].union.iVal;
                                    break;

                                case VARTYPE.VT_I4:
                                    value = vars[0].union.lVal;
                                    break;

                                case VARTYPE.VT_I8:
                                    value = vars[0].union.hVal;
                                    break;

                                case VARTYPE.VT_INT:
                                    value = vars[0].union.intVal;
                                    break;

                                case VARTYPE.VT_LPSTR:
                                    value = Marshal.PtrToStringAnsi(vars[0].union.pszVal);
                                    break;

                                case VARTYPE.VT_LPWSTR:
                                    value = Marshal.PtrToStringUni(vars[0].union.pwszVal);
                                    break;

                                case VARTYPE.VT_R4:
                                    value = vars[0].union.fltVal;
                                    break;

                                case VARTYPE.VT_R8:
                                    value = vars[0].union.dblVal;
                                    break;

                                case VARTYPE.VT_UI1:
                                    value = vars[0].union.bVal;
                                    break;

                                case VARTYPE.VT_UI2:
                                    value = vars[0].union.uiVal;
                                    break;

                                case VARTYPE.VT_UI4:
                                    value = vars[0].union.ulVal;
                                    break;

                                case VARTYPE.VT_UI8:
                                    value = vars[0].union.uhVal;
                                    break;

                                case VARTYPE.VT_UINT:
                                    value = vars[0].union.uintVal;
                                    break;

                                case VARTYPE.VT_UNKNOWN:
                                    value = Marshal.GetObjectForIUnknown(vars[0].union.punkVal);
                                    break;

                                default:
                                    value = null;
                                    break;
                            }
                        }
                        finally
                        {
                            PropVariantClear(ref vars[0]);
                        }

                        var property = new CompoundProperty(fmtid, name, stg.propid)
                        {
                            Value = value,
                            Changed = false,
                        };
                        Properties.InternalAdd(property);
                    }
                }
                while (fetched == 1);
            }
            finally
            {
                Marshal.ReleaseComObject(es);
            }
        }

        private static STGM GetMode(bool readOnly)
        {
            return readOnly ? STGM.STGM_READ | STGM.STGM_SHARE_DENY_NONE | STGM.STGM_DIRECT_SWMR : STGM.STGM_DIRECT_SWMR | STGM.STGM_READWRITE | STGM.STGM_SHARE_DENY_WRITE;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="CompoundStorage"/> class.
        /// </summary>
        /// <param name="filePath">The file path. May not be null.</param>
        public CompoundStorage(string filePath)
            : this(filePath, readOnly: true)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="CompoundStorage"/> class.
        /// </summary>
        /// <param name="filePath">The file path. May not be null.</param>
        /// <param name="readOnly">if set to <c>true</c> the file will be open for read only operations.</param>
        public CompoundStorage(string filePath, bool readOnly)
        {
            FilePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
            Properties = new CompoundPropertyCollection(readOnly);
            var mode = GetMode(readOnly: true);
            var guid = typeof(IPropertySetStorage).GUID;

            var hr = StgOpenStorageEx(FilePath, mode, STGFMT.STGFMT_ANY, 0, IntPtr.Zero, IntPtr.Zero, ref guid, out var propertySetStorage);
            if (hr == STG_E_FILENOTFOUND || hr == STG_E_PATHNOTFOUND)
                throw new FileNotFoundException(message: null, FilePath);

            if (hr != 0)
                throw new Win32Exception((int)hr);

            try
            {
                LoadPropertySet(propertySetStorage, CompoundProperty.SummaryInformationFormatId);
                LoadPropertySet(propertySetStorage, CompoundProperty.DocSummaryInformationFormatId);
            }
            finally
            {
                Marshal.ReleaseComObject(propertySetStorage);
            }

            // for some reason we can't read this one on the same COM ref?
            LoadProperties(CompoundProperty.UserDefinedPropertiesId);
        }

        /// <summary>
        /// Enumerates the property set format identifiers (FMTID) of the storage.
        /// </summary>
        /// <returns>A list of guids.</returns>
        public IEnumerable<Guid> EnumerateFormats()
        {
            var guid = typeof(IPropertySetStorage).GUID;
            var mode = GetMode(IsReadOnly);

            var hr = StgOpenStorageEx(FilePath, mode, STGFMT.STGFMT_ANY, 0, IntPtr.Zero, IntPtr.Zero, ref guid, out var propertySetStorage);
            if (hr == STG_E_FILENOTFOUND || hr == STG_E_PATHNOTFOUND)
                throw new FileNotFoundException(message: null, FilePath);

            if (hr != 0)
                throw new Win32Exception((int)hr);

            try
            {
                propertySetStorage.Enum(out var es);
                if (es != null)
                {
                    try
                    {
                        var stg = new STATPROPSETSTG();
                        int fetched;
                        do
                        {
                            hr = es.Next(1, ref stg, out fetched);
                            if (hr != 0 && hr != 1)
                                throw new Win32Exception((int)hr);

                            if (fetched == 1)
                            {
                                if (stg.fmtid == Guid.Empty)
                                    continue;

                                yield return stg.fmtid;
                            }
                        }
                        while (fetched == 1);
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(es);
                    }
                }
            }
            finally
            {
                if (propertySetStorage != null)
                {
                    Marshal.ReleaseComObject(propertySetStorage);
                }
            }
        }

        /// <summary>
        /// Loads the properties for a given format.
        /// </summary>
        /// <param name="formatId">The format id.</param>
        public void LoadProperties(Guid formatId)
        {
            var guid = typeof(IPropertySetStorage).GUID;
            var mode = GetMode(IsReadOnly);

            var hr = StgOpenStorageEx(FilePath, mode, STGFMT.STGFMT_ANY, 0, IntPtr.Zero, IntPtr.Zero, ref guid, out var propertySetStorage);
            if (hr == STG_E_FILENOTFOUND || hr == STG_E_PATHNOTFOUND)
                throw new FileNotFoundException(message: null, FilePath);

            if (hr != 0)
                throw new Win32Exception((int)hr);

            try
            {
                LoadPropertySet(propertySetStorage, formatId);
            }
            finally
            {
                Marshal.ReleaseComObject(propertySetStorage);
            }
        }

        private IEnumerable<CompoundProperty> TouchedProperties
        {
            get
            {
                foreach (var property in Properties)
                {
                    if (property.Changed)
                        yield return property;
                }

                foreach (var property in Properties._deleted)
                {
                    yield return property;
                }
            }
        }

        private static void FreePROPSEC(ref PROPSPEC spec)
        {
            Marshal.FreeCoTaskMem(spec.union.lpwstr);
            spec.union.lpwstr = IntPtr.Zero;
        }

        private static PROPSPEC GetPROPSEC(CompoundProperty prop)
        {
            var spec = new PROPSPEC();
            if (prop.IsKnownProperty)
            {
                spec.ulKind = PRSPEC.PRSPEC_PROPID;
                spec.union.propid = prop.Id;
            }
            else
            {
                spec.ulKind = PRSPEC.PRSPEC_LPWSTR;
                spec.union.lpwstr = Marshal.StringToCoTaskMemUni(prop.Name);
            }
            return spec;
        }

        private static PROPVARIANT GetPROPVARIANT(CompoundProperty prop)
        {
            var var = new PROPVARIANT();
            if (prop.Value == null)
                return var;

            var code = Type.GetTypeCode(prop.Value.GetType());
            switch (code)
            {
                case TypeCode.Boolean:
                    var.vt = VARTYPE.VT_BOOL;
                    var.union.boolVal = (bool)prop.Value ? (short)1 : (short)0;
                    break;

                case TypeCode.Byte:
                    var.vt = VARTYPE.VT_UI1;
                    var.union.bVal = (byte)prop.Value;
                    break;

                case TypeCode.Char:
                    var.vt = VARTYPE.VT_LPSTR;
                    var.union.pszVal = Marshal.StringToCoTaskMemAnsi(((char)prop.Value).ToString(CultureInfo.InvariantCulture));
                    break;

                case TypeCode.DateTime:
                    var.vt = VARTYPE.VT_FILETIME;
                    var.union.filetime = ((DateTime)prop.Value).ToFileTime();
                    break;

                case TypeCode.Decimal:
                    var dec = IntPtr.Zero;
                    Marshal.StructureToPtr((decimal)prop.Value, dec, fDeleteOld: false);
                    var = (PROPVARIANT)Marshal.PtrToStructure(dec, typeof(PROPVARIANT))!;
                    var.vt = VARTYPE.VT_DECIMAL;
                    break;

                case TypeCode.Double:
                    var.vt = VARTYPE.VT_R8;
                    var.union.dblVal = (double)prop.Value;
                    break;

                case TypeCode.Int16:
                    var.vt = VARTYPE.VT_I2;
                    var.union.iVal = (short)prop.Value;
                    break;

                case TypeCode.Int32:
                    var.vt = VARTYPE.VT_I4;
                    var.union.lVal = (int)prop.Value;
                    break;

                case TypeCode.Int64:
                    var.vt = VARTYPE.VT_I8;
                    var.union.hVal = (long)prop.Value;
                    break;

                case TypeCode.SByte:
                    var.vt = VARTYPE.VT_I1;
                    var.union.cVal = (sbyte)prop.Value;
                    break;

                case TypeCode.Single:
                    var.vt = VARTYPE.VT_R4;
                    var.union.fltVal = (float)prop.Value;
                    break;

                case TypeCode.String:
                    var.vt = VARTYPE.VT_LPSTR;
                    var.union.pszVal = Marshal.StringToCoTaskMemAnsi((string)prop.Value);
                    break;

                case TypeCode.UInt16:
                    var.vt = VARTYPE.VT_UI2;
                    var.union.uiVal = (ushort)prop.Value;
                    break;

                case TypeCode.UInt32:
                    var.vt = VARTYPE.VT_UI4;
                    var.union.ulVal = (uint)prop.Value;
                    break;

                case TypeCode.UInt64:
                    var.vt = VARTYPE.VT_UI8;
                    var.union.uhVal = (ulong)prop.Value;
                    break;

                case TypeCode.Object:
                    if (prop.Value is Guid)
                    {
                        var.vt = VARTYPE.VT_LPSTR;
                        var.union.pszVal = Marshal.StringToCoTaskMemAnsi(((Guid)prop.Value).ToString());
                    }
                    break;
            }
            return var;
        }

        private static bool OnlyDeletes(List<CompoundProperty> props)
        {
            foreach (var prop in props)
            {
                if (!prop.Deleted)
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Commits the changes if any.
        /// </summary>
        /// <returns>true if changes were detected and commited; otherwise false.</returns>
        public bool CommitChanges()
        {
            // group properties by property set (fmtid)
            var sets = new Dictionary<Guid, List<CompoundProperty>>();
            foreach (var property in TouchedProperties)
            {
                if (!sets.TryGetValue(property.FormatId, out var list))
                {
                    list = new List<CompoundProperty>();
                    sets.Add(property.FormatId, list);
                }

                list.Add(property);
            }

            if (sets.Count == 0)
                return false;

            var mode = GetMode(readOnly: false);

            var guid = typeof(IPropertySetStorage).GUID;
            var hr = StgOpenStorageEx(FilePath, mode, STGFMT.STGFMT_ANY, 0, IntPtr.Zero, IntPtr.Zero, ref guid, out var propertySetStorage);
            if (hr == STG_E_FILENOTFOUND || hr == STG_E_PATHNOTFOUND)
                throw new FileNotFoundException(message: null, FilePath);

            if (hr != 0)
                throw new Win32Exception((int)hr);

            try
            {
                foreach (var kvp in sets)
                {
                    var fmtid = kvp.Key;
                    const STGM Mode2 = STGM.STGM_READWRITE | STGM.STGM_SHARE_EXCLUSIVE;
                    hr = propertySetStorage.Open(ref fmtid, Mode2, out var propertyStorage);
                    if (hr == STG_E_FILENOTFOUND)
                    {
                        if (OnlyDeletes(kvp.Value))
                            continue;

                        var nullGuid = Guid.Empty;
                        hr = propertySetStorage.Create(ref fmtid, ref nullGuid, PROPSETFLAG_ANSI, Mode2, out propertyStorage);
                    }

                    if (hr != 0)
                        throw new Win32Exception((int)hr);

                    try
                    {
                        foreach (var property in kvp.Value)
                        {
                            if (property.Deleted)
                            {
                                var specs = new PROPSPEC[]
                                {
                                    GetPROPSEC(property),
                                };

                                try
                                {
                                    hr = propertyStorage.DeleteMultiple(1, specs);
                                    if (hr != 0)
                                        throw new Win32Exception((int)hr);
                                }
                                finally
                                {
                                    FreePROPSEC(ref specs[0]);
                                }
                            }
                        }

                        foreach (var property in kvp.Value)
                        {
                            if (property.Changed)
                            {
                                var specs = new PROPSPEC[]
                                {
                                    GetPROPSEC(property),
                                };

                                var vars = new PROPVARIANT[]
                                {
                                    GetPROPVARIANT(property),
                                };

                                try
                                {
                                    hr = propertyStorage.WriteMultiple(1, specs, vars, USERDEFINEDPROPERTIES_MIN);
                                    if (hr != 0)
                                        throw new Win32Exception((int)hr);
                                }
                                finally
                                {
                                    FreePROPSEC(ref specs[0]);
                                    PropVariantClear(ref vars[0]);
                                }
                            }
                        }

                        propertyStorage.Commit(0);
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(propertyStorage);
                    }
                }
            }
            finally
            {
                if (propertySetStorage != null)
                {
                    Marshal.ReleaseComObject(propertySetStorage);
                }
            }

            Properties.Commit();
            return true;
        }

        /// <summary>
        /// Gets the file path.
        /// </summary>
        /// <value>The file path.</value>
        public string FilePath { get; }

        /// <summary>
        /// Gets the properties.
        /// </summary>
        /// <value>The properties.</value>
        public CompoundPropertyCollection Properties { get; }

        /// <summary>
        /// Gets a value indicating whether this instance is read only.
        /// </summary>
        /// <value>
        /// 	<c>true</c> if this instance is read only; otherwise, <c>false</c>.
        /// </value>
        public bool IsReadOnly => Properties._readOnly;
    }
}
