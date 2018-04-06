using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;

namespace Meziantou.Framework.CodeDom.Tests
{
    internal static class AssertExtensions
    {
        public static void StringEquals(this Assert _, string expected, string actual)
        {
            if (expected == actual)
                return;

            var expectedFormat1 = Replace1(expected);
            var actualFormat1 = Replace1(actual);

            var expectedFormat2 = Replace2(expected);
            var actualFormat2 = Replace2(actual);

            var index = expectedFormat2.Zip(actualFormat2, (c1, c2) => c1 == c2).TakeWhile(b => b).Count() + 1;

            throw new AssertFailedException($@"Expect: <{expectedFormat1}>
Actual: <{actualFormat1}>

Expect: <{expectedFormat2}>
Actual: <{actualFormat2}>
         {new string(' ', index)}^");

            string Replace1(string value)
            {
                return value
                    .Replace(' ', '·')
                    .Replace('\t', '→')
                    .Replace("\r\n", "\\r\\n\r\n");
            }

            string Replace2(string value)
            {
                return value
                    .Replace(' ', '·')
                    .Replace('\t', '→')
                    .Replace("\r", "\\r")
                    .Replace("\n", "\\n");
            }
        }
    }
}
