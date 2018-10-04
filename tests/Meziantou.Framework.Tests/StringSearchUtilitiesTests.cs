using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Meziantou.Framework.Tests
{
    [TestClass]
    public class StringSearchUtilitiesTests
    {
        [DataTestMethod]
        [DataRow("", "", 0)]
        [DataRow("aa", "", 2)]
        [DataRow("", "aa", 2)]
        [DataRow("test", "Test", 1)]
        [DataRow("chien", "chienne", 2)]
        [DataRow("Cat", "Cat", 0)]
        [DataRow("Car", "Kart", 2)]
        public void Levenshtein_Tests(string word1, string word2, int expected)
        {
            Assert.AreEqual(expected, StringSearchUtilities.Levenshtein(word1, word2));
        }

        [DataTestMethod]
        [DataRow(0b101010u, 0b101010u, 0u)]
        [DataRow(0b010101u, 0b101010u, 6u)]
        [DataRow(0b1111u, 0b0u, 4u)]
        [DataRow(0b11111111u, 0b11110111u, 1u)]
        public void Hamming_Tests(uint word1, uint word2, uint expected)
        {
            Assert.AreEqual(expected, StringSearchUtilities.Hamming(word1, word2));
        }

        [DataTestMethod]
        [DataRow("ramer", "cases", 3u)]
        public void Hamming_Tests(string word1, string word2, int expected)
        {
            Assert.AreEqual(expected, StringSearchUtilities.Hamming(word1, word2));
        }

        [TestMethod]
        public void Soundex_Test()
        {
            var soundex = StringSearchUtilities.Soundex("", new Dictionary<char, byte>());
            Assert.AreEqual("0000", soundex);
        }

        [DataTestMethod]
        [DataRow("R163", "Robert")]
        [DataRow("R163", "Rupert")]
        [DataRow("R150", "Rubin")]
        [DataRow("H400", "Hello")]
        [DataRow("E460", "Euler")]
        [DataRow("E460", "Ellery")]
        [DataRow("G200", "Gauss")]
        [DataRow("G200", "Ghosh")]
        [DataRow("H416", "Hilbert")]
        [DataRow("H416", "Heilbronn")]
        [DataRow("K530", "Knuth")]
        [DataRow("K530", "Kant")]
        [DataRow("L300", "Ladd")]
        [DataRow("L300", "Lloyd")]
        [DataRow("L222", "Lukasiewicz")]
        [DataRow("L222", "Lissajous")]
        [DataRow("L222", "Lishsajwjous")]
        [DataRow("A462", "Allricht")]
        [DataRow("E166", "Eberhard")]
        [DataRow("E521", "Engebrethson")]
        [DataRow("H512", "Heimbach")]
        [DataRow("H524", "Hanselmann")]
        [DataRow("H524", "Henzelmann")]
        [DataRow("H431", "Hildebrand")]
        [DataRow("K152", "Kavanagh")]
        [DataRow("L530", " Lind, Van")]
        [DataRow("L222", "Lukaschowsky")]
        [DataRow("M235", "McDonnell")]
        [DataRow("M200", "McGee")]
        [DataRow("O165", "O'Brien")]
        [DataRow("O155", "Opnian")]
        [DataRow("O155", "Oppenheimer")]
        [DataRow("S460", "Swhgler")]
        [DataRow("R355", "Riedemanas")]
        [DataRow("Z300", "Zita")]
        [DataRow("Z325", "Zitzmeinn")]
        public void SoundexEnglish_Test(string expected, string value)
        {
            Assert.AreEqual(expected, StringSearchUtilities.SoundexEnglish(value));
        }

        [DataTestMethod]
        [DataRow("    ", "")]
        [DataRow("MRTN", "MARTIN")]
        [DataRow("BRNR", "BERNARD")]
        [DataRow("FR  ", "FAURE")]
        [DataRow("PRZ ", "PEREZ")]
        [DataRow("GR  ", "GROS")]
        [DataRow("CHP ", "CHAPUIS ")]
        [DataRow("BYR ", "BOYER")]
        [DataRow("KTR ", "GAUTHIER")]
        [DataRow("RY  ", "REY")]
        [DataRow("BRTL", "BARTHELEMY")]
        [DataRow("ANR ", "HENRY")]
        [DataRow("MLN ", "MOULIN")]
        [DataRow("RS  ", "ROUSSEAU")]
        [DataRow("RS  ", "YROUSSYEAU")]
        public void Soundex2_Test(string expected, string value)
        {
            Assert.AreEqual(expected, StringSearchUtilities.Soundex2(value));
        }

        [DataTestMethod]
        [DataRow("T830", "test")]
        [DataRow("H560", "Henry")]
        [DataRow("R000", "ray")]
        [DataRow("R000", "rey")]
        [DataRow("R000", "REY")]
        [DataRow("R000", "RAY")]
        public void SoundexFrench_Test(string expected, string value)
        {
            Assert.AreEqual(expected, StringSearchUtilities.SoundexFrench(value));
        }

        [DataTestMethod]
        [DataRow("TSTN", "testing")]
        [DataRow("0", "The")]
        [DataRow("KK", "quick")]
        [DataRow("BRN", "brown")]
        [DataRow("FKS", "fox")]
        [DataRow("JMPT", "jumped")]
        [DataRow("OFR", "over")]
        [DataRow("LS", "lazy")]
        [DataRow("TKS", "dogs")]
        [DataRow("TMP", "dump")]
        [DataRow("XFKS", "cheveux")]
        [DataRow("SKFK", "scheveux")]
        [DataRow("SFKS", "sciveux")]
        [DataRow("SKKF", "sccuveux")]
        [DataRow("KHLN", "khalens")]
        [DataRow("SFKS", "scyveux")]
        [DataRow("BSN", "buisson")]
        [DataRow("BB", "bebe")]
        [DataRow("LXS", "lacias")]
        [DataRow("TJKS", "dijkstra")]
        [DataRow("TJKS", "djikstra")]
        [DataRow("JKST", "dgikstra")]
        [DataRow("JKST", "dgekstra")]
        [DataRow("TKKS", "dgakstra")]
        [DataRow("KST", "ghost")]
        [DataRow("0R", "through")]
        [DataRow("NM", "gnome")]
        [DataRow("KSK", "chzkou")]
        [DataRow("BLKK", "blaggyguest")]
        [DataRow("AXXX", "atiatiotch")]
        public void Metaphone_Test(string expected, string value)
        {
            Assert.AreEqual(expected, StringSearchUtilities.Metaphone(value));
        }

        [DataTestMethod]
        [DataRow("Case", "case")]
        [DataRow("CASE", "Case")]
        [DataRow("caSe", "cAsE")]
        [DataRow("cookie", "quick")]
        [DataRow("T", "T")]
        [DataRow("Lorenza", "Lawrence")]
        [DataRow("Gary", "Cahra")]
        [DataRow("Aero", "Eure")]
        [DynamicData(nameof(MetaphoneList), DynamicDataSourceType.Method)]
        public void Metaphone_Test2(string value, string otherValue)
        {
            var v1 = StringSearchUtilities.Metaphone(value);
            var v2 = StringSearchUtilities.Metaphone(otherValue);
            Assert.AreEqual(v1, v2);
        }

        public static IEnumerable<object[]> MetaphoneList()
        {
            foreach (var item in new[] { "Wade", "Wait", "Waite", "Wat", "Whit", "Wiatt", "Wit", "Wittie", "Witty", "Wood", "Woodie", "Woody" })
                yield return new object[] { "White", item };

            foreach (var item in new[] { "Ailbert", "Alberik", "Albert", "Alberto", "Albrecht" })
                yield return new object[] { "Albert", item };

            foreach (var item in new[] {
                    "Cahra",
                    "Cara",
                    "Carey",
                    "Cari",
                    "Caria",
                    "Carie",
                    "Caro",
                    "Carree",
                    "Carri",
                    "Carrie",
                    "Carry",
                    "Cary",
                    "Cora",
                    "Corey",
                    "Cori",
                    "Corie",
                    "Correy",
                    "Corri",
                    "Corrie",
                    "Corry",
                    "Cory",
                    "Gray",
                    "Kara",
                    "Kare",
                    "Karee",
                    "Kari",
                    "Karia",
                    "Karie",
                    "Karrah",
                    "Karrie",
                    "Karry",
                    "Kary",
                    "Keri",
                    "Kerri",
                    "Kerrie",
                    "Kerry",
                    "Kira",
                    "Kiri",
                    "Kora",
                    "Kore",
                    "Kori",
                    "Korie",
                    "Korrie",
                    "Korry" })
            {
                yield return new object[] { "Gary", item };
            }

            foreach (var item in new[] {
                     "Gena",
                     "Gene",
                     "Genia",
                     "Genna",
                     "Genni",
                     "Gennie",
                     "Genny",
                     "Giana",
                     "Gianna",
                     "Gina",
                     "Ginni",
                     "Ginnie",
                     "Ginny",
                     "Jaine",
                     "Jan",
                     "Jana",
                     "Jane",
                     "Janey",
                     "Jania",
                     "Janie",
                     "Janna",
                     "Jany",
                     "Jayne",
                     "Jean",
                     "Jeana",
                     "Jeane",
                     "Jeanie",
                     "Jeanna",
                     "Jeanne",
                     "Jeannie",
                     "Jen",
                     "Jena",
                     "Jeni",
                     "Jenn",
                     "Jenna",
                     "Jennee",
                     "Jenni",
                     "Jennie",
                     "Jenny",
                     "Jinny",
                     "Jo Ann",
                     "Jo-Ann",
                     "Jo-Anne",
                     "Joan",
                     "Joana",
                     "Joane",
                     "Joanie",
                     "Joann",
                     "Joanna",
                     "Joanne",
                     "Joeann",
                     "Johna",
                     "Johnna",
                     "Joni",
                     "Jonie",
                     "Juana",
                     "June",
                     "Junia",
                     "Junie" })
            {
                yield return new object[] { "John", item };
            }

            foreach (var item in new[] {
                    "Mair",
                    "Maire",
                    "Mara",
                    "Mareah",
                    "Mari",
                    "Maria",
                    "Marie",
                    "Mary",
                    "Maura",
                    "Maure",
                    "Meara",
                    "Merrie",
                    "Merry",
                    "Mira",
                    "Moira",
                    "Mora",
                    "Moria",
                    "Moyra",
                    "Muire",
                    "Myra",
                    "Myrah" })
            {
                yield return new object[] { "Mary", item };
            }

            foreach (var item in new[] { "Pearcy", "Perris", "Piercy", "Pierz", "Pryse" })
                yield return new object[] { "Paris", item };

            foreach (var item in new[] { "Peadar", "Peder", "Pedro", "Peter", "Petr", "Peyter", "Pieter", "Pietro", "Piotr" })
                yield return new object[] { "Peter", item };

            foreach (var item in new[] { "Ray", "Rey", "Roi", "Roy", "Ruy" })
                yield return new object[] { "Ray", item };

            foreach (var item in new[] { "Siusan", "Sosanna", "Susan", "Susana", "Susann", "Susanna", "Susannah", "Susanne", "Suzann", "Suzanna", "Suzanne", "Zuzana" })
                yield return new object[] { "Susan", item };

            foreach (var item in new[] { "Rota", "Rudd", "Ryde" })
                yield return new object[] { "Wright", item };

            foreach (var item in new[] { "Celene", "Celina", "Celine", "Selena", "Selene", "Selina", "Seline", "Suellen", "Xylina" })
                yield return new object[] { "Xalan", item };
        }
    }
}
