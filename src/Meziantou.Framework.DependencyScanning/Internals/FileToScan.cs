namespace Meziantou.Framework.DependencyScanning.Internals
{
    internal struct FileToScan<T>
        where T : IEnabledScannersArray
    {
        public FileToScan(T scanners, string fullPath)
        {
            Scanners = scanners;
            FullPath = fullPath;
        }

        public T Scanners { get; }
        public string FullPath { get; }
    }
}
