namespace Meziantou.Framework.Tests;

public class LoremIpsumGeneratorTests
{
    [Fact]
    public void Sentence_ShouldHaveExpectedShape()
    {
        var sentence = LoremIpsumGenerator.Sentence(wordCount: 4);

        Assert.EndsWith(".", sentence, StringComparison.Ordinal);
        var content = sentence[..^1];
        Assert.True(char.IsUpper(content[0]));
        Assert.Equal(4, content.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public void Paragraph_ShouldHaveExpectedSentenceAndWordCount()
    {
        var paragraph = LoremIpsumGenerator.Paragraph(wordCount: 5, sentenceCount: 3);

        Assert.EndsWith(".", paragraph, StringComparison.Ordinal);
        var sentences = paragraph.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Equal(3, sentences.Length);

        foreach (var sentence in sentences)
        {
            Assert.True(char.IsUpper(sentence[0]));
            Assert.Equal(5, sentence.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length);
        }
    }

    [Fact]
    public void Paragraphs_ShouldReturnRequestedCount()
    {
        var paragraphs = LoremIpsumGenerator.Paragraphs(wordsPerSentence: 3, sentencesPerParagraph: 2, paragraphCount: 4).ToArray();

        Assert.Equal(4, paragraphs.Length);
        Assert.All(paragraphs, Assert.NotEmpty);
    }
}
