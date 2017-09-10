using Meziantou.Framework.Utilities;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Meziantou.Framework.Tests.Utilities
{
    [TestClass]
    public class IOUtilitiesTests
    {
        [DataTestMethod]
        [DataRow(@"c:\dir1\", @"c:\dir1\dir2", true)]
        [DataRow(@"c:\a\", @"c:\dir1\dir2", false)]
        [DataRow(@"c:\a\", @"c:\dir1\..\a\dir2", true)]
        [DataRow(@"c:\dir1", @"c:\dir1", true)]
        public void IsChildPathOf(string parent, string child, bool expectedResult)
        {
            var result = IOUtilities.IsChildPathOf(parent, child);

            Assert.AreEqual(expectedResult, result);
        }

        [DataTestMethod]
        [DataRow(@"c:\dir1", @"c:\dir1", true)]
        [DataRow(@"c:\dir1\", @"c:\dir1\dir2", false)]
        [DataRow(@"c:\a\", @"d:\a\", false)]
        [DataRow(@"c:\a\", @"c:\dir1\..\a\", true)]
        public void ArePathEqual(string path1, string path2, bool expectedResult)
        {
            var result = IOUtilities.ArePathEqual(path1, path2);

            Assert.AreEqual(expectedResult, result);
        }

        [DataTestMethod]
        [DataRow(@"c:\dir1", @"c:\dir1", "")]
        [DataRow(@"c:\dir1\", @"c:\dir1\dir2", "dir2")]
        [DataRow(@"c:\a\", @"d:\a\", @"d:\a\")]
        [DataRow(@"c:\a\", @"c:\dir1\..\a\dir2", "dir2")]
        [DataRow(@"c:\a\b\c\", @"c:\a\dir2", @"..\..\dir2")]
        public void MakeRelativePath(string path1, string path2, string expectedResult)
        {
            var result = IOUtilities.MakeRelativePath(path1, path2);

            Assert.AreEqual(expectedResult, result);
        }


        [DataTestMethod]
        [DataRow("sample.txt", "sample.txt")]
        [DataRow("sample/.txt", "sample_x47_.txt")]
        [DataRow("COM1", "_COM1_")]
        public void ToValidFileName(string fileName, string expectedResult)
        {
            var result = IOUtilities.ToValidFileName(fileName);
            Assert.AreEqual(expectedResult, result);
        }
    }
}
