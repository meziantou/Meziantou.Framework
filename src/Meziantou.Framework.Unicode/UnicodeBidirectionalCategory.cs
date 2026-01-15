namespace Meziantou.Framework;

/// <summary>Represents a Unicode bidirectional category.</summary>
public enum UnicodeBidirectionalCategory : byte
{
    LeftToRight,
    RightToLeft,
    RightToLeftArabic,
    EuropeanNumber,
    EuropeanSeparator,
    EuropeanTerminator,
    ArabicNumber,
    CommonSeparator,
    ParagraphSeparator,
    SegmentSeparator,
    WhiteSpace,
    OtherNeutral,
    NonspacingMark,
    LeftToRightEmbedding,
    LeftToRightOverride,
    RightToLeftEmbedding,
    RightToLeftOverride,
    PopDirectionalFormat,
    LeftToRightIsolate,
    RightToLeftIsolate,
    FirstStrongIsolate,
    PopDirectionalIsolate,
    BoundaryNeutral,
}
