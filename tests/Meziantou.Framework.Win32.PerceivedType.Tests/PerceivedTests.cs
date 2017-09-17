using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Meziantou.Framework.Win32.Tests
{
    [TestClass]
    public class PerceivedTests
    {
        [TestMethod]
        public void GetPerceivedType01()
        {
            var perceived = Perceived.GetPerceivedType(".txt");
            Assert.AreEqual(PerceivedType.Text, perceived.PerceivedType);
        }

        [TestMethod]
        public void GetPerceivedType02()
        {
            var perceived = Perceived.GetPerceivedType(".avi");
            Assert.AreEqual(PerceivedType.Video, perceived.PerceivedType);
        }
    }
}
