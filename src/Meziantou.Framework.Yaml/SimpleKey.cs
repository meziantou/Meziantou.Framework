namespace Meziantou.Framework.Yaml;

/// <summary>Represents a simple key.</summary>
internal class SimpleKey
{

    /// <summary>Gets or sets a value indicating whether this instance is possible.</summary>
    /// <value>
    /// 	<c>true</c> if this instance is possible; otherwise, <c>false</c>.
    /// </value>
    public bool IsPossible { get; set; }

    /// <summary>Gets or sets a value indicating whether this instance is required.</summary>
    /// <value>
    /// 	<c>true</c> if this instance is required; otherwise, <c>false</c>.
    /// </value>
    public bool IsRequired { get; private set; }

    /// <summary>Gets or sets the token number.</summary>
    /// <value>The token number.</value>
    public int TokenNumber { get; private set; }

    /// <summary>Gets or sets the mark that indicates the location of the simple key.</summary>
    /// <value>The mark.</value>
    public Mark Mark { get; private set; }

    public int CharacterIndex { get; private set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="SimpleKey"/> class.
    /// </summary>
    public SimpleKey()
    {
    }

    /// <summary>Replaces every value of this instance, so the scanner can reuse it for the next potential key.</summary>
    public void Set(bool isPossible, bool isRequired, int tokenNumber, Mark mark, int characterIndex)
    {
        IsPossible = isPossible;
        IsRequired = isRequired;
        TokenNumber = tokenNumber;
        Mark = mark;
        CharacterIndex = characterIndex;
    }
}