using System.Collections;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>https://dom.spec.whatwg.org/#interface-domtokenlist</summary>
[IdlInterface("DOMTokenList", "https://dom.spec.whatwg.org/#domtokenlist")]
public sealed class DomTokenList : IReadOnlyList<string>
{
    private readonly Element _element;
    private readonly string _attributeName;
    private readonly IReadOnlyCollection<string>? _supportedTokens;
    private List<string> _tokenSet = [];
    private string? _tokenSetSource;

    internal DomTokenList(Element element, string attributeName, IReadOnlyCollection<string>? supportedTokens = null)
    {
        _element = element;
        _attributeName = attributeName;
        _supportedTokens = supportedTokens;
    }

    /// <summary>
    /// The token set. The spec updates it in the attribute change steps; the token set is a pure function of the
    /// attribute value, so it is recomputed lazily when the attribute value changed.
    /// </summary>
    private List<string> TokenSet
    {
        get
        {
            var value = _element.GetAttributeValue(_attributeName);
            if (!ReferenceEquals(value, _tokenSetSource))
            {
                _tokenSet = value is null ? [] : OrderedSets.Parse(value);
                _tokenSetSource = value;
            }

            return _tokenSet;
        }
    }

    /// <summary>Gets the number of tokens in the list.</summary>
    [IdlMember("length", "https://dom.spec.whatwg.org/#dom-domtokenlist-length")]
    public uint Length => (uint)TokenSet.Count;

    /// <summary>Gets the number of tokens in the list.</summary>
    [NotInIdl]
    public int Count => TokenSet.Count;

    /// <summary>Returns the token at the given index, or null when the index is out of range.</summary>
    [IdlMember("item", "https://dom.spec.whatwg.org/#dom-domtokenlist-item")]
    public string? Item(uint index) => index < (uint)TokenSet.Count ? TokenSet[(int)index] : null;

    /// <summary>Gets the token at the given index, or null when the index is out of range.</summary>
    [IdlMember("item", "https://dom.spec.whatwg.org/#dom-domtokenlist-item")]
    [System.Runtime.CompilerServices.IndexerName("ItemAt")]
    public string? this[uint index] => Item(index);

    /// <summary>Gets the token at the given index.</summary>
    [NotInIdl]
    [System.Runtime.CompilerServices.IndexerName("ItemAt")]
    public string this[int index] => TokenSet[index];

    /// <summary>Returns whether the list contains the given token.</summary>
    [IdlMember("contains", "https://dom.spec.whatwg.org/#dom-domtokenlist-contains")]
    public bool Contains(string token) => TokenSet.Contains(token);

    private static void ValidateToken(string token)
    {
        ArgumentNullException.ThrowIfNull(token);
        if (token.Length == 0)
            throw DomException.Syntax("The token cannot be empty");

        foreach (var c in token)
        {
            if (InfraAscii.IsAsciiWhitespace(c))
                throw DomException.InvalidCharacter("The token cannot contain whitespace");
        }
    }

    /// <summary>Adds the given tokens to the list, ignoring those already present.</summary>
    [IdlMember("add", "https://dom.spec.whatwg.org/#dom-domtokenlist-add")]
    public void Add(params IReadOnlyList<string> tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        foreach (var token in tokens)
        {
            ValidateToken(token);
        }

        var tokenSet = TokenSet;
        foreach (var token in tokens)
        {
            if (!tokenSet.Contains(token))
            {
                tokenSet.Add(token);
            }
        }

        RunUpdateSteps(tokenSet);
    }

    /// <summary>Removes the given tokens from the list.</summary>
    [IdlMember("remove", "https://dom.spec.whatwg.org/#dom-domtokenlist-remove")]
    public void Remove(params IReadOnlyList<string> tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        foreach (var token in tokens)
        {
            ValidateToken(token);
        }

        var tokenSet = TokenSet;
        foreach (var token in tokens)
        {
            tokenSet.Remove(token);
        }

        RunUpdateSteps(tokenSet);
    }

    /// <summary>https://dom.spec.whatwg.org/#dom-domtokenlist-toggle</summary>
    [IdlMember("toggle", "https://dom.spec.whatwg.org/#dom-domtokenlist-toggle")]
    public bool Toggle(string token, bool? force = null)
    {
        ValidateToken(token);
        var tokenSet = TokenSet;
        if (tokenSet.Contains(token))
        {
            if (force is null or false)
            {
                tokenSet.Remove(token);
                RunUpdateSteps(tokenSet);
                return false;
            }

            return true;
        }

        if (force is null or true)
        {
            tokenSet.Add(token);
            RunUpdateSteps(tokenSet);
            return true;
        }

        return false;
    }

    /// <summary>https://dom.spec.whatwg.org/#dom-domtokenlist-replace</summary>
    [IdlMember("replace", "https://dom.spec.whatwg.org/#dom-domtokenlist-replace")]
    public bool Replace(string token, string newToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(newToken);
        if (token.Length == 0 || newToken.Length == 0)
            throw DomException.Syntax("The token cannot be empty");

        if (token.AsSpan().IndexOfAny(" \t\n\f\r") >= 0 || newToken.AsSpan().IndexOfAny(" \t\n\f\r") >= 0)
            throw DomException.InvalidCharacter("The token cannot contain whitespace");

        var tokenSet = TokenSet;
        var index = tokenSet.IndexOf(token);
        if (index < 0)
            return false;

        // Replace within the ordered set: the new token takes the position of the first of token or newToken
        var newTokenIndex = tokenSet.IndexOf(newToken);
        if (newTokenIndex >= 0 && newTokenIndex != index)
        {
            if (newTokenIndex < index)
            {
                tokenSet.RemoveAt(index);
            }
            else
            {
                tokenSet[index] = newToken;
                tokenSet.RemoveAt(newTokenIndex);
            }
        }
        else
        {
            tokenSet[index] = newToken;
        }

        RunUpdateSteps(tokenSet);
        return true;
    }

    /// <summary>https://dom.spec.whatwg.org/#dom-domtokenlist-supports</summary>
    [IdlMember("supports", "https://dom.spec.whatwg.org/#dom-domtokenlist-supports")]
    public bool Supports(string token)
    {
        ArgumentNullException.ThrowIfNull(token);
        if (_supportedTokens is null)
            throw new TypeErrorException($"The '{_attributeName}' attribute does not define supported tokens");

        return _supportedTokens.Contains(InfraAscii.ToAsciiLowercase(token));
    }

    /// <summary>Gets or sets the value of the associated attribute, as a string.</summary>
    [IdlMember("value", "https://dom.spec.whatwg.org/#dom-domtokenlist-value")]
    public string Value
    {
        get => _element.GetAttributeValue(_attributeName) ?? "";
        set => _element.SetAttributeValue(_attributeName, value ?? "");
    }

    /// <summary>https://dom.spec.whatwg.org/#concept-dtl-update</summary>
    private void RunUpdateSteps(List<string> tokenSet)
    {
        if (_element.GetAttributeValue(_attributeName) is null && tokenSet.Count == 0)
            return;

        var value = string.Join(' ', tokenSet);
        _element.SetAttributeValue(_attributeName, value);
        _tokenSet = tokenSet;
        _tokenSetSource = _element.GetAttributeValue(_attributeName);
    }

    /// <inheritdoc/>
    public override string ToString() => Value;

    /// <inheritdoc/>
    [IdlMember("iterable", "https://dom.spec.whatwg.org/#domtokenlist")]
    public IEnumerator<string> GetEnumerator()
    {
        for (var i = 0; i < TokenSet.Count; i++)
        {
            yield return TokenSet[i];
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>Ordered sets (https://dom.spec.whatwg.org/#ordered-sets).</summary>
internal static class OrderedSets
{
    /// <summary>https://dom.spec.whatwg.org/#concept-ordered-set-parser</summary>
    public static List<string> Parse(string input)
    {
        var tokens = new List<string>();
        foreach (var range in input.AsSpan().SplitAny(" \t\n\f\r"))
        {
            var token = input[range];
            if (token.Length > 0 && !tokens.Contains(token))
            {
                tokens.Add(token);
            }
        }

        return tokens;
    }
}
