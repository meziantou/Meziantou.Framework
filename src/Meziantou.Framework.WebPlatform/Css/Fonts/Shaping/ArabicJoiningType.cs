namespace Meziantou.Framework.WebPlatform.Css.Fonts;

/// <summary>The joining types of the shaping of the cursive scripts (<see cref="ArabicShaper.GetJoiningType"/>), with the joining groups of Syriac that have their own forms: the values index the state machine of the joining.</summary>
internal enum ArabicJoiningType : byte
{
    /// <summary>Non_Joining.</summary>
    U,

    /// <summary>Left_Joining.</summary>
    L,

    /// <summary>Right_Joining.</summary>
    R,

    /// <summary>Dual_Joining and Join_Causing.</summary>
    D,

    /// <summary>Right_Joining, Joining_Group=ALAPH.</summary>
    Alaph,

    /// <summary>Right_Joining, Joining_Group=DALATH_RISH.</summary>
    DalathRish,

    /// <summary>Transparent.</summary>
    T,
}
