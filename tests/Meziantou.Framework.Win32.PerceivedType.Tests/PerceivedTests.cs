using TestUtilities;
using Xunit;

namespace Meziantou.Framework.Win32.Tests
{
    public class PerceivedTests
    {
        [RunIfFact(FactOperatingSystem.Windows)]
        public void GetPerceivedType01()
        {
            var perceived = Perceived.GetPerceivedType(".txt");
            Assert.Equal(PerceivedType.Text, perceived.PerceivedType);
        }

        [RunIfFact(FactOperatingSystem.Windows)]
        public void GetPerceivedType02()
        {
            var perceived = Perceived.GetPerceivedType(".avi");
            Assert.Equal(PerceivedType.Video, perceived.PerceivedType);
        }
    }
}
