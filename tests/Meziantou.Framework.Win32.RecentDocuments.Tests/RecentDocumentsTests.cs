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
}
