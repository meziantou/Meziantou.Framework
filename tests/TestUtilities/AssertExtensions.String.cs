using System;
using System.Linq;
using Xunit;

namespace TestUtilities
{
    public static partial class AssertExtensions
    {
        public static void StringEquals(string expected, string actual)
        {
            StringEquals(expected, actual, ignoreNewLines: true);
        }

        public static void StringEquals(string expected, string actual, bool ignoreNewLines)
        {
            if (ignoreNewLines)
            {
                expected = expected.Replace("\r\n", "\n", StringComparison.Ordinal);
                actual = actual.Replace("\r\n", "\n", StringComparison.Ordinal);
            }

            if (string.Equals(expected, actual, StringComparison.Ordinal))
                return;

            var expectedFormat1 = Replace1(expected);
            var actualFormat1 = Replace1(actual);

            var expectedFormat2 = Replace2(expected);
            var actualFormat2 = Replace2(actual);

            var index = expectedFormat2.Zip(actualFormat2, (c1, c2) => c1 == c2).TakeWhile(b => b).Count() + 1;
            Assert.True(false, $"Expect: <{expectedFormat1}>\n" +
                               $"Actual: <{actualFormat1}>\n" +
                               $"\n" +
                               $"Expect: <{expectedFormat2}>\n" +
                               $"Actual: <{actualFormat2}>\n" +
                               $"         {new string(' ', index)}^");

            static string Replace1(string value)
            {
                return value
                    .Replace(' ', '·')
                    .Replace('\t', '→')
                    .Replace("\r\n", "\\r\\n\r\n", StringComparison.Ordinal);
            }

            static string Replace2(string value)
            {
                return value
                    .Replace(' ', '·')
                    .Replace('\t', '→')
                    .Replace("\r", "\\r", StringComparison.Ordinal)
                    .Replace("\n", "\\n", StringComparison.Ordinal);
            }
        }
    }

}
