using Xunit;
using Xunit.Abstractions;

namespace TestUtilities
{
    public sealed class Dummy
    {
        private readonly ITestOutputHelper _outputHelper;

        public Dummy(ITestOutputHelper outputHelper)
        {
            _outputHelper = outputHelper;
        }

        [Fact(Skip = "For testing purpose")]
        public void Test()
        {
            _outputHelper.WriteLine("This test output one line");
            Assert.True(false, "This is a dummy test");
        }
    }
}
