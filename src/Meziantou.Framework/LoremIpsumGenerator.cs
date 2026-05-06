#pragma warning disable CA5394 // Random is insecure
namespace Meziantou.Framework;

public static class LoremIpsumGenerator
{
    private static readonly string[] Words =
    [
        "lorem", "ipsum", "dolor", "sit", "amet", "consectetur", "adipiscing", "elit",
        "sed", "do", "eiusmod", "tempor", "incididunt", "ut", "labore", "et", "dolore",
        "magna", "aliqua", "enim", "ad", "minim", "veniam", "quis", "nostrud",
        "exercitation", "ullamco", "laboris", "nisi", "aliquip", "ex", "ea", "commodo",
        "consequat", "duis", "aute", "irure", "in", "reprehenderit", "voluptate",
        "velit", "esse", "cillum", "fugiat", "nulla", "pariatur", "excepteur", "sint",
        "occaecat", "cupidatat", "non", "proident", "sunt", "culpa", "qui", "officia",
        "deserunt", "mollit", "anim", "id", "est", "laborum",
    ];

    public static IEnumerable<string> Paragraphs(int wordsPerSentence, int sentencesPerParagraph, int paragraphCount)
    {
        for (var i = 0; i < paragraphCount; i++)
        {
            yield return Paragraph(wordsPerSentence, sentencesPerParagraph);
        }
    }

    public static string Paragraph(int wordCount, int sentenceCount)
    {
        var sentences = new List<string>(sentenceCount);
        for (var i = 0; i < sentenceCount; i++)
        {
            sentences.Add(Sentence(wordCount));
        }

        return string.Join(' ', sentences);
    }

    public static string Sentence(int wordCount)
    {
        var words = new List<string>(wordCount);
        for (var i = 0; i < wordCount; i++)
        {
            var word = Words[Random.Shared.Next(Words.Length)];
            words.Add(i is 0 ? char.ToUpperInvariant(word[0]) + word[1..] : word);
        }

        return string.Join(' ', words) + ".";
    }
}
