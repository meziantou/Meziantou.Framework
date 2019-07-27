using Xunit;

namespace Meziantou.Framework.Win32.Tests
{
    public class PerceivedTests
    {
        [Fact]
        public void GetPerceivedType01()
        {
            var perceived = Perceived.GetPerceivedType(".txt");
            Assert.Equal(PerceivedType.Text, perceived.PerceivedType);
        }

        [Fact]
        public void GetPerceivedType02()
        {
            var perceived = Perceived.GetPerceivedType(".avi");
            Assert.Equal(PerceivedType.Video, perceived.PerceivedType);
        }
    }
}
