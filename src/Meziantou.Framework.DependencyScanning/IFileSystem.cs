using System.Collections.Generic;
using System.IO;

namespace Meziantou.Framework.DependencyScanning;

public interface IFileSystem
{
    Stream OpenRead(string path);
    Stream OpenReadWrite(string path);
    IEnumerable<string> GetFiles(string path, string pattern);
}
