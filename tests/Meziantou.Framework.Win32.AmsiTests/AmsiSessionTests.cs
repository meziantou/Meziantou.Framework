using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Meziantou.Framework.Win32.AmsiTests
{
    [TestClass]
    public class AmsiSessionTests
    {
        [TestMethod]
        //[Ignore("The test doesn't work on CI")]
        public void AmsiShouldDetectMalware()
        {
            using (var session = AmsiSession.Create("MyApplication"))
            {
                // https://en.wikipedia.org/wiki/EICAR_test_file
                Assert.IsTrue(session.IsMalware(@"X5O!P%@AP[4\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*", "EICAR"));
                Assert.IsTrue(session.IsMalware(Encoding.ASCII.GetBytes(@"X5O!P%@AP[4\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*"), "EICAR"));
                Assert.IsFalse(session.IsMalware("0000", "EICAR"));
            }
        }
    }
}
