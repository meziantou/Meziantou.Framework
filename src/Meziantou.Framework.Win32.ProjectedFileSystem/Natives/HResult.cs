using System.Runtime.InteropServices;

namespace Meziantou.Framework.Win32.ProjectedFileSystem
{
    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct HResult
    {
        public int Value { get; }

        public HResult(int value)
        {
            Value = value;
        }

        public bool IsSuccess => Value >= 0;

        public static HResult S_OK => new HResult();
        public static HResult E_INVALIDARG => new HResult(unchecked((int)0x80070057));
        public static HResult E_FILENOTFOUND => HRESULT_FROM_WIN32(2);
        public static HResult E_OUTOFMEMORY => new HResult(unchecked((int)0x8007000E));

        public void EnsureSuccess()
        {
            if (!IsSuccess)
            {
                Marshal.ThrowExceptionForHR(Value);
            }
        }

        private const int FACILITY_WIN32 = 7;

        private static HResult HRESULT_FROM_WIN32(int x)
        {
            //  return (((x) & 0x0000FFFF) | (FACILITY_WIN32 << 16) | 0x80000000);
            int hr;
            if (x <= 0)
            {
                hr = x;
            }
            else
            {
                hr = (int)(((uint)x & 0x0000FFFF) | (FACILITY_WIN32 << 16) | 0x80000000);
            }

            return new HResult(hr);
        }
    }

    //class Program
    //{
    //    // TODO notification on rename/delete/... 

    //    // https://github.com/Microsoft/Windows-classic-samples/blob/master/Samples/ProjectedFileSystem/regfsProvider.cpp
    //    static void Main(string[] args)
    //    {
    //        var guid = Guid.NewGuid();
    //        var fullPath = Path.Combine(Path.GetTempPath(), "projFS", guid.ToString("N"));
    //        Directory.CreateDirectory(fullPath);

    //        using (var vfs = new SampleVirtualFileSystem(fullPath))
    //        {
    //            vfs.Initialize();
    //            var results = Directory.EnumerateFileSystemEntries(fullPath).ToList();
    //            foreach (var result in results)
    //            {
    //                var fi = new FileInfo(result);
    //                var length = fi.Length;

    //            }

    //            try
    //            {
    //                var fi2 = new FileInfo(Path.Combine(fullPath, "unknownfile.txt"));
    //                var length2 = fi2.Length;
    //                Debug.Fail("File does not exist");
    //            }
    //            catch (FileNotFoundException)
    //            {
    //            }

    //            var bytes = File.ReadAllBytes(Path.Combine(fullPath, "a"));
    //            using (var stream = File.OpenRead(Path.Combine(fullPath, "b")))
    //            {
    //                var b1 = stream.ReadByte();
    //                var b2 = stream.ReadByte();
    //            }
    //        }
    //    }
    //}
}
