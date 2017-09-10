using Meziantou.Framework.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;

namespace Meziantou.Framework.Tests.IO
{
    [TestClass]
    public class TeeTextWriterTests
    {
        [TestMethod]
        public void WriteTest01()
        {
            using (var sw1 = new StringWriter())
            using (var sw2 = new StringWriter())
            using (var tee = new TeeTextWriter(sw1, sw2))
            {

                tee.Write("abc");
                tee.Flush();

                Assert.AreEqual("abc", sw1.ToString());
                Assert.AreEqual("abc", sw2.ToString());
            }
        }
    }
}
