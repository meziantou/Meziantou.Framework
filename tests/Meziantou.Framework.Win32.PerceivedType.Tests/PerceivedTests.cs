using TestUtilities;
using FluentAssertions;

namespace Meziantou.Framework.Win32.Tests
{
    public class PerceivedTests
    {
        [RunIfFact(FactOperatingSystem.Windows)]
        public void GetPerceivedType01()
        {
            var perceived = Perceived.GetPerceivedType(".txt");
            perceived.PerceivedType.Should().Be(PerceivedType.Text);
        }

        [RunIfFact(FactOperatingSystem.Windows)]
        public void GetPerceivedType02()
        {
            var perceived = Perceived.GetPerceivedType(".avi");
            perceived.PerceivedType.Should().Be(PerceivedType.Video);
        }
    }
}
