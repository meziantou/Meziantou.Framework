using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Meziantou.Framework.Tests
{
    public class TemporaryDirectoryTests
    {
        [Fact]
        public void CreateInParallel()
        {
            const int Iterations = 100;
            var dirs = new TemporaryDirectory[Iterations];

            Parallel.For(0, Iterations, new ParallelOptions { MaxDegreeOfParallelism = 50 }, i =>
            {
                dirs[i] = TemporaryDirectory.Create();
            });

            try
            {
                Assert.Equal(Iterations, dirs.DistinctBy(dir => dir.FullPath).Count());

                foreach (var dir in dirs)
                {
                    Assert.All(dirs, dir => Assert.True(Directory.Exists(dir.FullPath)));
                }
            }
            finally
            {
                foreach (var item in dirs)
                {
                    item?.Dispose();
                }
            }
        }

        [Fact]
        public void DisposedDeletedDirectory()
        {
            using var dir = TemporaryDirectory.Create();
            Directory.Delete(dir.FullPath);
        }
    }
}
