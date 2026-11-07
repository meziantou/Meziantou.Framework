using System.Security.Cryptography;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The parameter sets that the decoder has received, by identifier (Rec. ITU-T H.265, clause 7.4.2.4.2 "Order of VPS, SPS and
/// PPS RBSPs and their activation"). A parameter set that is received again with the content it has is the same parameter
/// set: the pictures that refer to it keep what was derived from it.
/// </summary>
internal sealed class HevcParameterSetStore
{
    private const int DigestLength = 32;

    private readonly HevcVideoParameterSet?[] _vps = new HevcVideoParameterSet?[HevcConstants.MaxVpsCount];
    private readonly HevcSequenceParameterSet?[] _sps = new HevcSequenceParameterSet?[HevcConstants.MaxSpsCount];
    private readonly HevcPictureParameterSet?[] _pps = new HevcPictureParameterSet?[HevcConstants.MaxPpsCount];
    private readonly byte[] _vpsDigests = new byte[HevcConstants.MaxVpsCount * DigestLength];
    private readonly byte[] _spsDigests = new byte[HevcConstants.MaxSpsCount * DigestLength];
    private readonly byte[] _ppsDigests = new byte[HevcConstants.MaxPpsCount * DigestLength];

    /// <summary>Takes the parameter sets of another store, which are not parsed again: a parameter set is immutable.</summary>
    public void CopyFrom(HevcParameterSetStore other)
    {
        ArgumentNullException.ThrowIfNull(other);
        other._vps.CopyTo(_vps, 0);
        other._sps.CopyTo(_sps, 0);
        other._pps.CopyTo(_pps, 0);
        other._vpsDigests.CopyTo(_vpsDigests, 0);
        other._spsDigests.CopyTo(_spsDigests, 0);
        other._ppsDigests.CopyTo(_ppsDigests, 0);
    }

    /// <summary>Gets the video parameter set <paramref name="id"/>, or <see langword="null"/>.</summary>
    public HevcVideoParameterSet? GetVps(int id) => _vps[id];

    /// <summary>Gets the sequence parameter set <paramref name="id"/>, or <see langword="null"/>.</summary>
    public HevcSequenceParameterSet? GetSps(int id) => _sps[id];

    /// <summary>Gets the picture parameter set <paramref name="id"/>, or <see langword="null"/>.</summary>
    public HevcPictureParameterSet? GetPps(int id) => _pps[id];

    /// <summary>Reads a video parameter set from the payload of its NAL unit and stores it.</summary>
    public void AddVps(ReadOnlySpan<byte> rbsp)
    {
        var vps = HevcVideoParameterSet.Parse(rbsp);
        if (IsNew(rbsp, _vpsDigests, vps.VideoParameterSetId, _vps[vps.VideoParameterSetId] is not null))
        {
            _vps[vps.VideoParameterSetId] = vps;
        }
    }

    /// <summary>Reads a sequence parameter set from the payload of its NAL unit and stores it.</summary>
    public void AddSps(ReadOnlySpan<byte> rbsp)
    {
        var sps = HevcSequenceParameterSet.ParseRbsp(rbsp);
        if (IsNew(rbsp, _spsDigests, sps.SeqParameterSetId, _sps[sps.SeqParameterSetId] is not null))
        {
            _sps[sps.SeqParameterSetId] = sps;
        }
    }

    /// <summary>Reads a picture parameter set from the payload of its NAL unit and stores it.</summary>
    public void AddPps(ReadOnlySpan<byte> rbsp)
    {
        var pps = HevcPictureParameterSet.ParseRbsp(rbsp);
        if (IsNew(rbsp, _ppsDigests, pps.PicParameterSetId, _pps[pps.PicParameterSetId] is not null))
        {
            _pps[pps.PicParameterSetId] = pps;
        }
    }

    // Compares the content of a parameter set with the content of the one that has its identifier, by a digest of their
    // payloads, and remembers the digest of a new content
    private static bool IsNew(ReadOnlySpan<byte> rbsp, byte[] digests, int id, bool exists)
    {
        Span<byte> digest = stackalloc byte[DigestLength];
        SHA256.HashData(rbsp, digest);
        var stored = digests.AsSpan(id * DigestLength, DigestLength);
        if (exists && digest.SequenceEqual(stored))
            return false;

        digest.CopyTo(stored);
        return true;
    }
}
