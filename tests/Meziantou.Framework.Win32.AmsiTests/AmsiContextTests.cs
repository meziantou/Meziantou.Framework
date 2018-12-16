using System;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Meziantou.Framework.Win32.AmsiTests
{
    [TestClass]
    [DoNotParallelize]
    public class AmsiContextTests
    {
        [ClassInitialize]
        public static void ClassInitialize(TestContext _)
        {
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BUILD_DEFINITIONNAME")))
            {
                Assert.Inconclusive("The tests does not work on Azure pipeline");
            }
        }

        [TestMethod]
        public void AmsiShouldDetectMalware_Buffer()
        {
            using (var application = AmsiContext.Create("MyApplication"))
            {
                // https://en.wikipedia.org/wiki/EICAR_test_file
                Assert.IsTrue(application.IsMalware(Encoding.ASCII.GetBytes(@"X5O!P%@AP[4\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*"), "EICAR"));
                Assert.IsFalse(application.IsMalware(new byte[] { 0, 0, 0, 0 }, "EICAR"));
            }
        }

        [TestMethod]
        public void AmsiShouldDetectMalware_String()
        {
            using (var application = AmsiContext.Create("MyApplication"))
            {
                // https://en.wikipedia.org/wiki/EICAR_test_file
                Assert.IsTrue(application.IsMalware(@"X5O!P%@AP[4\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*", "EICAR"));
                Assert.IsFalse(application.IsMalware("0000", "EICAR"));
            }
        }

        [TestMethod]
        public void AmsiSessionShouldDetectMalware_Buffer()
        {
            using (var application = AmsiContext.Create("MyApplication"))
            using (var session = application.CreateSession())
            {
                // https://en.wikipedia.org/wiki/EICAR_test_file
                Assert.IsTrue(session.IsMalware(Encoding.ASCII.GetBytes(@"X5O!P%@AP[4\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*"), "EICAR"));
                Assert.IsFalse(session.IsMalware(new byte[] { 0, 0, 0, 0 }, "EICAR"));
            }
        }

        [TestMethod]
        public void AmsiSessionShouldDetectMalware_String()
        {
            using (var application = AmsiContext.Create("MyApplication"))
            using (var session = application.CreateSession())
            {
                // https://en.wikipedia.org/wiki/EICAR_test_file
                Assert.IsTrue(session.IsMalware(@"X5O!P%@AP[4\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*", "EICAR"));
                Assert.IsFalse(session.IsMalware("0000", "EICAR"));
            }
        }
    }
}
