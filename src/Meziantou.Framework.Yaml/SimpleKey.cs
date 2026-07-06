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
    public bool IsRequired { get; }

    /// <summary>Gets or sets the token number.</summary>
    /// <value>The token number.</value>
    public int TokenNumber { get; }

    /// <summary>Gets or sets the mark that indicates the location of the simple key.</summary>
    /// <value>The mark.</value>
    public Mark Mark { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="SimpleKey"/> class.
    /// </summary>
    public SimpleKey()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SimpleKey"/> class.
    /// </summary>
    public SimpleKey(bool isPossible, bool isRequired, int tokenNumber, Mark mark)
    {
        IsPossible = isPossible;
        IsRequired = isRequired;
        TokenNumber = tokenNumber;
        Mark = mark;
    }
}