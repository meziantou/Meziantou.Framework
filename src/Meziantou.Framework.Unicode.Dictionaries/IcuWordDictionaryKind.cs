namespace Meziantou.Framework;

/// <summary>Identifies a word list of ICU (<c>icu4c/source/data/brkitr/dictionaries/*dict.txt</c>).</summary>
public enum IcuWordDictionaryKind
{
    /// <summary>The words of Chinese and Japanese (<c>cjdict.txt</c>), with their cost: Han, Hiragana and Katakana.</summary>
    ChineseJapanese,

    /// <summary>The words of Thai (<c>thaidict.txt</c>).</summary>
    Thai,

    /// <summary>The words of Lao (<c>laodict.txt</c>).</summary>
    Lao,

    /// <summary>The words of Khmer (<c>khmerdict.txt</c>).</summary>
    Khmer,

    /// <summary>The words of Burmese (<c>burmesedict.txt</c>), written in the Myanmar script.</summary>
    Burmese,
}
