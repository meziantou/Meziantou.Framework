namespace Meziantou.Framework.Tests;

public class LoremIpsumGeneratorTests
{
    [Fact]
    public void Documentation_Generate()
    {
        #region LoremIpsumGenerator_Usage
        var sentence = LoremIpsumGenerator.Sentence(wordCount: 5);
        Assert.HasCount(5, sentence.Split(' '));
        Assert.EndsWith(".", sentence);

        // 3 sentences of 8 words
        var paragraph = LoremIpsumGenerator.Paragraph(wordCount: 8, sentenceCount: 3);
        Assert.HasCount(24, paragraph.Split(' '));

        var paragraphs = LoremIpsumGenerator.Paragraphs(wordsPerSentence: 8, sentencesPerParagraph: 3, paragraphCount: 2);
        Assert.HasCount(2, paragraphs);
        #endregion
    }

    [Fact]
    public void Sentence_ShouldHaveExpectedShape()
    {
        var sentence = LoremIpsumGenerator.Sentence(wordCount: 4);

        Assert.EndsWith(".", sentence);
        var content = sentence[..^1];
        Assert.True(char.IsUpper(content[0]));
        Assert.HasCount(4, content.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public void Paragraph_ShouldHaveExpectedSentenceAndWordCount()
    {
        var paragraph = LoremIpsumGenerator.Paragraph(wordCount: 5, sentenceCount: 3);

        Assert.EndsWith(".", paragraph);
        var sentences = paragraph.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.HasCount(3, sentences);

        foreach (var sentence in sentences)
        {
            Assert.True(char.IsUpper(sentence[0]));
            Assert.HasCount(5, sentence.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }
    }

    [Fact]
    public void Paragraphs_ShouldReturnRequestedCount()
    {
        var paragraphs = LoremIpsumGenerator.Paragraphs(wordsPerSentence: 3, sentencesPerParagraph: 2, paragraphCount: 4).ToArray();

        Assert.HasCount(4, paragraphs);
        Assert.All(paragraphs, item => Assert.NotEmpty(item));
    }
}
