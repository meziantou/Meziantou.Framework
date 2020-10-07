using System.IO.Enumeration;

namespace Meziantou.Framework.DependencyScanning
{
    public delegate bool FileSystemEntryPredicate(ref FileSystemEntry entry);
}
