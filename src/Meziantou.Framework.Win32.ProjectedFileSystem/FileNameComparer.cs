using System.Collections.Generic;

namespace Meziantou.Framework.Win32.ProjectedFileSystem
{
    public sealed class FileNameComparer : IComparer<string>
    {
        private FileNameComparer()
        {
        }

        public static IComparer<string> Instance { get; } = new FileNameComparer();

        public int Compare(string x, string y)
        {
            return NativeMethods.PrjFileNameCompare(x, y);
        }
    }
}
