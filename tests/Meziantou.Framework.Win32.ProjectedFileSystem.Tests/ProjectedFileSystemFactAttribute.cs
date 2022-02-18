using Xunit;

namespace Meziantou.Framework.Win32.ProjectedFileSystem
{
    [AttributeUsage(AttributeTargets.All)]
    public sealed class ProjectedFileSystemFactAttribute : FactAttribute
    {
        public ProjectedFileSystemFactAttribute()
        {
            var guid = Guid.NewGuid();
            var fullPath = Path.Combine(Path.GetTempPath(), "projFS", guid.ToString("N"));
            try
            {
                Directory.CreateDirectory(fullPath);

                try
                {
                    using var vfs = new SampleVirtualFileSystem(fullPath);
                    var options = new ProjectedFileSystemStartOptions();
                    try
                    {
                        vfs.Start(options);
                    }
                    catch (NotSupportedException ex)
                    {
                        Skip = ex.Message;
                    }
                }
                catch
                {
                }
            }
            finally
            {
                try
                {
                    Directory.Delete(fullPath, recursive: true);
                }
                catch
                {
                }
            }
        }
    }
}
