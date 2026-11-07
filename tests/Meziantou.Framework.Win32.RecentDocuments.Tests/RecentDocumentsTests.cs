namespace Meziantou.Framework.Win32.Tests;

public sealed class RecentDocumentsTests
{
    [Fact]
    public void AddToRecentDocuments_ThrowsWhenPathIsNull()
    {
        // A null path used to reach SHAddToRecentDocs as a null pointer, which clears all usage data
        Assert.Throws<ArgumentNullException>(() => RecentDocuments.AddToRecentDocuments(null!));
    }

    [Fact]
    public void AddToRecentDocuments_ThrowsWhenPathIsEmpty()
    {
        Assert.Throws<ArgumentException>(() => RecentDocuments.AddToRecentDocuments(""));
    }

    [Fact]
    public void AddToRecentDocuments_ThrowsWhenPathContainsNullCharacter()
    {
        // The shell stops reading at the null character, so the call would register a different item
        var exception = Assert.Throws<ArgumentException>(() => RecentDocuments.AddToRecentDocuments("file\0.txt"));
        Assert.Equal("path", exception.ParamName);
    }

    [Fact]
    public void AddToRecentDocumentsWithAppId_ThrowsWhenPathIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => RecentDocuments.AddToRecentDocuments(null!, "Company.App"));
    }

    [Fact]
    public void AddToRecentDocumentsWithAppId_ThrowsWhenPathIsEmpty()
    {
        Assert.Throws<ArgumentException>(() => RecentDocuments.AddToRecentDocuments("", "Company.App"));
    }

    [Fact]
    public void AddToRecentDocumentsWithAppId_ThrowsWhenPathContainsNullCharacter()
    {
        var exception = Assert.Throws<ArgumentException>(() => RecentDocuments.AddToRecentDocuments("file\0.txt", "Company.App"));
        Assert.Equal("path", exception.ParamName);
    }

    [Fact]
    public void AddToRecentDocumentsWithAppId_ThrowsWhenAppIdIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => RecentDocuments.AddToRecentDocuments("file.txt", null!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Company App")]
    [InlineData("Company\0App")]
    public void AddToRecentDocumentsWithAppId_ThrowsWhenAppIdIsInvalid(string applicationUserModelId)
    {
        var exception = Assert.Throws<ArgumentException>(() => RecentDocuments.AddToRecentDocuments("file.txt", applicationUserModelId));
        Assert.Equal("applicationUserModelId", exception.ParamName);
    }

    [Fact]
    public void AddToRecentDocumentsWithAppId_ThrowsWhenAppIdIsTooLong()
    {
        var exception = Assert.Throws<ArgumentException>(() => RecentDocuments.AddToRecentDocuments("file.txt", new string('a', 129)));
        Assert.Equal("applicationUserModelId", exception.ParamName);
    }
}
