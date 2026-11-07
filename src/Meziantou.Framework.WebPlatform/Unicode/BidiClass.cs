namespace Meziantou.Framework.WebPlatform.Unicode;

/// <summary>
/// The Bidi_Class property values (https://www.unicode.org/reports/tr9/#Bidirectional_Character_Types) with the short names
/// of the rules of UAX #9: the values are the ones of <see cref="UnicodeBidirectionalCategory"/>, so a cast converts them.
/// </summary>
internal enum BidiClass : byte
{
    L = (byte)UnicodeBidirectionalCategory.LeftToRight,
    R = (byte)UnicodeBidirectionalCategory.RightToLeft,
    AL = (byte)UnicodeBidirectionalCategory.RightToLeftArabic,
    EN = (byte)UnicodeBidirectionalCategory.EuropeanNumber,
    ES = (byte)UnicodeBidirectionalCategory.EuropeanSeparator,
    ET = (byte)UnicodeBidirectionalCategory.EuropeanTerminator,
    AN = (byte)UnicodeBidirectionalCategory.ArabicNumber,
    CS = (byte)UnicodeBidirectionalCategory.CommonSeparator,
    NSM = (byte)UnicodeBidirectionalCategory.NonspacingMark,
    BN = (byte)UnicodeBidirectionalCategory.BoundaryNeutral,
    B = (byte)UnicodeBidirectionalCategory.ParagraphSeparator,
    S = (byte)UnicodeBidirectionalCategory.SegmentSeparator,
    WS = (byte)UnicodeBidirectionalCategory.WhiteSpace,
    ON = (byte)UnicodeBidirectionalCategory.OtherNeutral,
    LRE = (byte)UnicodeBidirectionalCategory.LeftToRightEmbedding,
    LRO = (byte)UnicodeBidirectionalCategory.LeftToRightOverride,
    RLE = (byte)UnicodeBidirectionalCategory.RightToLeftEmbedding,
    RLO = (byte)UnicodeBidirectionalCategory.RightToLeftOverride,
    PDF = (byte)UnicodeBidirectionalCategory.PopDirectionalFormat,
    LRI = (byte)UnicodeBidirectionalCategory.LeftToRightIsolate,
    RLI = (byte)UnicodeBidirectionalCategory.RightToLeftIsolate,
    FSI = (byte)UnicodeBidirectionalCategory.FirstStrongIsolate,
    PDI = (byte)UnicodeBidirectionalCategory.PopDirectionalIsolate,
}
