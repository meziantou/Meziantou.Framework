namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The general profile, tier and level of a coded video sequence (Rec. ITU-T H.265, clause 7.3.3 "Profile, tier and level
/// syntax"). The values of the sub-layers are read and not kept: they do not take part in the decoding.
/// </summary>
internal sealed class HevcProfileTierLevel
{
    private HevcProfileTierLevel()
    {
    }

    /// <summary>Gets <c>general_profile_space</c>: always 0, the only value that this version of the specification defines.</summary>
    public int GeneralProfileSpace { get; private init; }

    /// <summary>Gets <c>general_tier_flag</c>.</summary>
    public bool GeneralTierFlag { get; private init; }

    /// <summary>Gets <c>general_profile_idc</c> (Annex A).</summary>
    public int GeneralProfileIdc { get; private init; }

    /// <summary>Gets <c>general_profile_compatibility_flag[ j ]</c>: the flag <c>j</c> is the bit <c>31 - j</c>.</summary>
    public uint GeneralProfileCompatibilityFlags { get; private init; }

    /// <summary>
    /// Gets the 48 bits that follow the compatibility flags, from <c>general_progressive_source_flag</c> to
    /// <c>general_inbld_flag</c>: the first one is the bit 47.
    /// </summary>
    public ulong GeneralConstraintIndicatorFlags { get; private init; }

    /// <summary>Gets <c>general_level_idc</c>: 30 times the level number.</summary>
    public int GeneralLevelIdc { get; private init; }

    /// <summary>Gets <c>general_progressive_source_flag</c>.</summary>
    public bool GeneralProgressiveSourceFlag => (GeneralConstraintIndicatorFlags & (1UL << 47)) is not 0;

    /// <summary>Gets <c>general_interlaced_source_flag</c>.</summary>
    public bool GeneralInterlacedSourceFlag => (GeneralConstraintIndicatorFlags & (1UL << 46)) is not 0;

    /// <summary>Reads the syntax structure <c>profile_tier_level( profilePresentFlag, maxNumSubLayersMinus1 )</c>.</summary>
    public static HevcProfileTierLevel Parse(ref HevcBitReader reader, bool profilePresentFlag, int maxNumSubLayersMinus1)
    {
        var profileSpace = 0;
        var tierFlag = false;
        var profileIdc = 0;
        uint compatibilityFlags = 0;
        ulong constraintFlags = 0;
        if (profilePresentFlag)
        {
            profileSpace = (int)reader.ReadBits(2);
            tierFlag = reader.ReadFlag();
            profileIdc = (int)reader.ReadBits(5);
            compatibilityFlags = reader.ReadBits(32);

            // The number of bits of the constraint flags does not depend on the profile: 4 source flags, 43 flags that the
            // profiles define or reserve, and general_inbld_flag or a reserved bit
            constraintFlags = ((ulong)reader.ReadBits(16) << 32) | reader.ReadBits(32);

            // Clause 7.4.4: a decoder ignores the coded video sequences of another profile space
            if (profileSpace is not 0)
                throw HevcErrors.Unsupported("profile space " + profileSpace.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        var levelIdc = (int)reader.ReadBits(8);
        Span<bool> subLayerProfilePresentFlag = stackalloc bool[8];
        Span<bool> subLayerLevelPresentFlag = stackalloc bool[8];
        for (var i = 0; i < maxNumSubLayersMinus1; i++)
        {
            subLayerProfilePresentFlag[i] = reader.ReadFlag();
            subLayerLevelPresentFlag[i] = reader.ReadFlag();
        }

        if (maxNumSubLayersMinus1 > 0)
        {
            // reserved_zero_2bits[ i ]
            reader.Skip(2 * (8 - maxNumSubLayersMinus1));
        }

        for (var i = 0; i < maxNumSubLayersMinus1; i++)
        {
            if (subLayerProfilePresentFlag[i])
            {
                reader.Skip(88);
            }

            if (subLayerLevelPresentFlag[i])
            {
                reader.Skip(8);
            }
        }

        return new HevcProfileTierLevel
        {
            GeneralProfileSpace = profileSpace,
            GeneralTierFlag = tierFlag,
            GeneralProfileIdc = profileIdc,
            GeneralProfileCompatibilityFlags = compatibilityFlags,
            GeneralConstraintIndicatorFlags = constraintFlags,
            GeneralLevelIdc = levelIdc,
        };
    }
}
