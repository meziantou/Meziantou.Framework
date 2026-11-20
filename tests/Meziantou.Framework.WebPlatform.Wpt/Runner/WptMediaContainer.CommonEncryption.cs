using System.Buffers.Binary;
using Meziantou.Framework.WebPlatform.Media;

namespace Meziantou.Framework.WebPlatform.Wpt.Runner;

// The Common Encryption of the fragmented MP4 files of the tests (ISO/IEC 23001-7): the pssh boxes of the movie are the
// Initialization Data, the tenc box of a protected sample entry gives the scheme and the default key, the sample groups
// 'seig' change the key of samples, and the sample auxiliary information (the senc box, or the saiz and saio boxes) gives the
// initialization vector and the subsamples of each sample. The library decrypts the samples with the keys of the sessions.
internal static partial class WptMediaContainer
{
    /// <summary>
    /// The protection of a track: the scheme (schm), the defaults of its samples (tenc), and the 'seig' sample group
    /// descriptions of its sample table, which the sample groups of the fragments can refer to.
    /// </summary>
    private sealed record TrackProtection(MediaEncryptionScheme Scheme, bool IsProtected, int PerSampleIvSize, byte[] KeyId, byte[]? ConstantIv, int CryptByteBlock, int SkipByteBlock)
    {
        public IReadOnlyList<Mp4SegmentParser.SampleGroupProtection> MovieGroups { get; init; } = [];
    }

    private sealed partial class Mp4SegmentParser
    {
        // The limits protect against hostile inputs: the test files have a few dozen samples per fragment
        private const int MaxSubsamples = 1024;
        private const int MaxSampleGroups = 1024;

        /// <summary>Reads the protection of the first sample entry of a track (sinf: schm and tenc), or null when the scheme is not supported.</summary>
        private static TrackProtection? ReadTrackProtection(ReadOnlySpan<byte> trak)
        {
            var mdia = FindBox(trak, "mdia"u8);
            var isVideo = Slice(FindBox(mdia, "hdlr"u8), 8, 4).SequenceEqual("vide"u8);
            var stbl = FindBox(FindBox(mdia, "minf"u8), "stbl"u8);
            var stsd = FindBox(stbl, "stsd"u8);
            var entryOffset = 8;
            if (!TryReadBox(stsd, ref entryOffset, out _, out var entry))
                return null;

            var sinf = FindBox(Slice(entry, isVideo ? 78 : 28, entry.Length), "sinf"u8);

            // schm: version, flags, scheme_type, scheme_version
            var schm = FindBox(sinf, "schm"u8);
            var schemeType = Slice(schm, 4, 4);
            MediaEncryptionScheme scheme;
            if (schemeType.SequenceEqual("cenc"u8))
            {
                scheme = MediaEncryptionScheme.Cenc;
            }
            else if (schemeType.SequenceEqual("cbcs"u8))
            {
                scheme = MediaEncryptionScheme.Cbcs;
            }
            else
            {
                return null;
            }

            // tenc: version, flags, reserved, crypt and skip byte blocks (version 1), isProtected, Per_Sample_IV_Size, KID (16),
            // then the constant IV of a protected track without per-sample IV
            var tenc = FindBox(FindBox(sinf, "schi"u8), "tenc"u8);
            if (tenc.Length < 24)
                return null;

            var crypt = tenc[0] > 0 ? tenc[5] >> 4 : 0;
            var skip = tenc[0] > 0 ? tenc[5] & 0xF : 0;
            var isProtected = tenc[6] != 0;
            var ivSize = tenc[7];
            var keyId = tenc.Slice(8, 16).ToArray();
            byte[]? constantIv = null;
            if (isProtected && ivSize == 0)
            {
                if (tenc.Length < 25 || tenc[24] is not (8 or 16) || tenc.Length < 25 + tenc[24])
                    return null;

                constantIv = tenc.Slice(25, tenc[24]).ToArray();
            }

            if (ivSize is not (0 or 8 or 16))
                return null;

            if (ReadSampleGroupDescriptions(FindSampleGroupBox(stbl, "sgpd"u8)) is not { } movieGroups)
                return null;

            return new TrackProtection(scheme, isProtected, ivSize, keyId, constantIv, crypt, skip) { MovieGroups = movieGroups };
        }

        /// <summary>The pssh boxes of a movie box, as one <c>cenc</c> Initialization Data (https://www.w3.org/TR/eme-initdata-cenc/), or an empty list.</summary>
        private static List<MediaEncryptionInitData> ReadPsshBoxes(ReadOnlySpan<byte> movie)
        {
            var boxes = new List<byte>();
            var offset = 0;
            while (true)
            {
                var start = offset;
                if (!TryReadBox(movie, ref offset, out var type, out _))
                    break;

                if (type.SequenceEqual("pssh"u8))
                {
                    boxes.AddRange(movie[start..offset]);
                }
            }

            return boxes.Count == 0 ? [] : [new MediaEncryptionInitData("cenc", boxes.ToArray())];
        }

        /// <summary>The 'seig' sample group description of a sample: the protection of the samples of the group.</summary>
        internal sealed record SampleGroupProtection(bool IsProtected, int PerSampleIvSize, byte[] KeyId, byte[]? ConstantIv, int CryptByteBlock, int SkipByteBlock);

        /// <summary>
        /// Adds the encryption of the samples of a track fragment to their frames: the key of the sample group of each sample
        /// (sbgp and sgpd 'seig') or of the track, and the initialization vector and the subsamples of the sample auxiliary
        /// information (senc, else saiz and saio). Returns false when the information is missing or does not match the samples.
        /// </summary>
        private static bool AddEncryption(ReadOnlySpan<byte> traf, TrackProtection protection, List<MediaCodedFrame> frames, int firstFrame, List<(int Offset, int Size)> samples, ReadOnlyMemory<byte> segment)
        {
            var count = frames.Count - firstFrame;
            if (samples.Count != count)
                return false;

            var groups = ReadSampleGroups(traf, count, protection.MovieGroups);
            if (groups is null)
                return false;

            // The protection of each sample: its group, or the defaults of the track
            var sampleProtections = new SampleGroupProtection[count];
            var trackDefaults = new SampleGroupProtection(protection.IsProtected, protection.PerSampleIvSize, protection.KeyId, protection.ConstantIv, protection.CryptByteBlock, protection.SkipByteBlock);
            for (var i = 0; i < count; i++)
            {
                sampleProtections[i] = groups[i] ?? trackDefaults;
            }

            var auxiliary = ReadSampleEncryptionBox(traf, sampleProtections) ?? ReadAuxiliaryInformation(traf, sampleProtections, segment.Span);
            for (var i = 0; i < count; i++)
            {
                var sampleProtection = sampleProtections[i];
                if (!sampleProtection.IsProtected)
                    continue;

                var (offset, size) = samples[i];
                if (offset < 0 || size < 0 || offset > segment.Length - size)
                    return false;

                byte[]? iv;
                IReadOnlyList<MediaSubsample> subsamples = [];
                if (auxiliary is not null)
                {
                    (iv, subsamples) = auxiliary[i];
                }
                else if (sampleProtection.PerSampleIvSize == 0)
                {
                    iv = sampleProtection.ConstantIv;
                }
                else
                {
                    // The initialization vector of the sample is missing
                    return false;
                }

                if (iv is null)
                    return false;

                try
                {
                    var encryption = new MediaSampleEncryption(protection.Scheme, sampleProtection.KeyId, iv, subsamples)
                    {
                        CryptByteBlock = sampleProtection.CryptByteBlock,
                        SkipByteBlock = sampleProtection.SkipByteBlock,
                    };
                    frames[firstFrame + i] = frames[firstFrame + i] with { Encryption = encryption, Data = segment.Slice(offset, size) };
                }
                catch (ArgumentException)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// The 'seig' sample group of each sample (null for the samples of no group, which use the defaults of the track), or
        /// null when the groups are not valid. The group description indexes greater than 0x10000 refer to the sgpd box of the
        /// track fragment.
        /// </summary>
        private static SampleGroupProtection?[]? ReadSampleGroups(ReadOnlySpan<byte> traf, int sampleCount, IReadOnlyList<SampleGroupProtection> movieGroups)
        {
            var result = new SampleGroupProtection?[sampleCount];
            var sbgp = FindSampleGroupBox(traf, "sbgp"u8);
            if (sbgp.IsEmpty)
                return result;

            if (ReadSampleGroupDescriptions(FindSampleGroupBox(traf, "sgpd"u8)) is not { } descriptions)
                return null;

            // sbgp: version, flags, grouping_type, grouping_type_parameter (version 1), entry_count, then (sample_count,
            // group_description_index)
            var groupPosition = sbgp[0] == 1 ? 12 : 8;
            var groupCount = ReadBigEndian(sbgp, groupPosition, 4);
            groupPosition += 4;
            if (groupCount > MaxSampleGroups)
                return null;

            var sample = 0L;
            for (var i = 0u; i < groupCount && sample < sampleCount; i++)
            {
                var samplesOfGroup = (long)ReadBigEndian(sbgp, groupPosition, 4);
                var index = (uint)ReadBigEndian(sbgp, groupPosition + 4, 4);
                groupPosition += 8;
                SampleGroupProtection? description = null;
                if (index > 0x10000)
                {
                    if (index - 0x10001 >= descriptions.Count)
                        return null;

                    description = descriptions[(int)(index - 0x10001)];
                }
                else if (index != 0)
                {
                    // A description of the sample table of the movie
                    if (index - 1 >= movieGroups.Count)
                        return null;

                    description = movieGroups[(int)(index - 1)];
                }

                for (var j = 0L; j < samplesOfGroup && sample < sampleCount; j++)
                {
                    result[sample++] = description;
                }
            }

            return result;
        }

        /// <summary>The sbgp or sgpd box of the 'seig' grouping type of a box (a track fragment, or a sample table), or an empty span.</summary>
        private static ReadOnlySpan<byte> FindSampleGroupBox(ReadOnlySpan<byte> parent, ReadOnlySpan<byte> boxType)
        {
            var offset = 0;
            while (TryReadBox(parent, ref offset, out var type, out var payload))
            {
                if (type.SequenceEqual(boxType) && Slice(payload, 4, 4).SequenceEqual("seig"u8))
                    return payload;
            }

            return default;
        }

        /// <summary>The 'seig' sample group descriptions of a sgpd box (an empty list without box), or null when the box is not valid.</summary>
        private static List<SampleGroupProtection>? ReadSampleGroupDescriptions(ReadOnlySpan<byte> sgpd)
        {
            // sgpd: version, flags, grouping_type, default_length (version 1), default_sample_description_index (version 2),
            // entry_count, then the entries (with their length when the default length is 0)
            var descriptions = new List<SampleGroupProtection>();
            if (sgpd.IsEmpty)
                return descriptions;

            var version = sgpd[0];
            var position = 8;
            var defaultLength = 0u;
            if (version == 1)
            {
                defaultLength = (uint)ReadBigEndian(sgpd, position, 4);
                position += 4;
            }
            else if (version >= 2)
            {
                position += 4;
            }

            var entryCount = ReadBigEndian(sgpd, position, 4);
            position += 4;
            if (entryCount > MaxSampleGroups)
                return null;

            for (var i = 0u; i < entryCount; i++)
            {
                var length = (long)defaultLength;
                if (version == 1 && defaultLength == 0)
                {
                    length = (long)ReadBigEndian(sgpd, position, 4);
                    position += 4;
                }

                // CencSampleEncryptionInformationGroupEntry: reserved, crypt and skip byte blocks, isProtected,
                // Per_Sample_IV_Size, KID (16), then the constant IV
                var entry = Slice(sgpd, position, sgpd.Length - position);
                if (entry.Length < 20)
                    return null;

                byte[]? constantIv = null;
                var entryLength = 20;
                if (entry[2] != 0 && entry[3] == 0)
                {
                    if (entry.Length < 21 || entry[20] is not (8 or 16) || entry.Length < 21 + entry[20])
                        return null;

                    constantIv = entry.Slice(21, entry[20]).ToArray();
                    entryLength = 21 + entry[20];
                }

                if (entry[3] is not (0 or 8 or 16))
                    return null;

                descriptions.Add(new SampleGroupProtection(entry[2] != 0, entry[3], entry.Slice(4, 16).ToArray(), constantIv, entry[1] >> 4, entry[1] & 0xF));
                position += version == 1 && length > 0 ? (int)Math.Min(length, int.MaxValue / 2) : entryLength;
            }

            return descriptions;
        }

        /// <summary>The initialization vector and the subsamples of each sample, read from the senc box, or null when the track fragment has no valid senc box.</summary>
        private static (byte[]? Iv, IReadOnlyList<MediaSubsample> Subsamples)[]? ReadSampleEncryptionBox(ReadOnlySpan<byte> traf, SampleGroupProtection[] protections)
        {
            var senc = FindBox(traf, "senc"u8);
            if (senc.Length < 8)
                return null;

            // senc: version, flags (0x2: the subsamples), sample_count, then the IV and the subsamples of each sample
            var hasSubsamples = (ReadBigEndian(senc, 1, 3) & 0x2) != 0;
            if (ReadBigEndian(senc, 4, 4) != (ulong)protections.Length)
                return null;

            var reader = senc[8..];
            var result = new (byte[]? Iv, IReadOnlyList<MediaSubsample> Subsamples)[protections.Length];
            for (var i = 0; i < protections.Length; i++)
            {
                if (!TryReadAuxiliaryInformation(ref reader, protections[i], hasSubsamples, out result[i]))
                    return null;
            }

            return result;
        }

        /// <summary>
        /// The initialization vector and the subsamples of each sample, read from the sample auxiliary information that the
        /// saiz and saio boxes locate (the offsets are relative to the movie fragment box), or null.
        /// </summary>
        private static (byte[]? Iv, IReadOnlyList<MediaSubsample> Subsamples)[]? ReadAuxiliaryInformation(ReadOnlySpan<byte> traf, SampleGroupProtection[] protections, ReadOnlySpan<byte> segment)
        {
            var saiz = FindBox(traf, "saiz"u8);
            var saio = FindBox(traf, "saio"u8);
            if (saiz.Length < 9 || saio.Length < 8)
                return null;

            // saiz: version, flags (0x1: aux_info_type and its parameter), default_sample_info_size, sample_count, sizes
            var sizePosition = (ReadBigEndian(saiz, 1, 3) & 0x1) != 0 ? 12 : 4;
            var defaultSize = Slice(saiz, sizePosition, 1) is [var value] ? value : 0;
            if (ReadBigEndian(saiz, sizePosition + 1, 4) != (ulong)protections.Length)
                return null;

            // saio: version, flags (0x1: aux_info_type and its parameter), entry_count, offsets (4 or 8 bytes): one entry for
            // the contiguous information of all the samples
            var offsetPosition = (ReadBigEndian(saio, 1, 3) & 0x1) != 0 ? 12 : 4;
            if (ReadBigEndian(saio, offsetPosition, 4) != 1)
                return null;

            var auxiliaryOffset = ReadBigEndian(saio, offsetPosition + 4, saio[0] == 0 ? 4 : 8);
            if (auxiliaryOffset >= (ulong)segment.Length)
                return null;

            var reader = segment[(int)auxiliaryOffset..];
            var result = new (byte[]? Iv, IReadOnlyList<MediaSubsample> Subsamples)[protections.Length];
            for (var i = 0; i < protections.Length; i++)
            {
                var size = defaultSize != 0 ? defaultSize : (int)ReadBigEndian(saiz, sizePosition + 5 + i, 1);
                if (size > reader.Length)
                    return null;

                var information = reader[..size];
                reader = reader[size..];
                if (!TryReadAuxiliaryInformation(ref information, protections[i], hasSubsamples: size > protections[i].PerSampleIvSize, out result[i]))
                    return null;
            }

            return result;
        }

        /// <summary>Reads the auxiliary information of a sample: the IV (Per_Sample_IV_Size bytes, else the constant IV), then the subsamples.</summary>
        private static bool TryReadAuxiliaryInformation(ref ReadOnlySpan<byte> reader, SampleGroupProtection protection, bool hasSubsamples, out (byte[]? Iv, IReadOnlyList<MediaSubsample> Subsamples) information)
        {
            information = default;
            var ivSize = protection.IsProtected ? protection.PerSampleIvSize : 0;
            if (reader.Length < ivSize)
                return false;

            var iv = ivSize > 0 ? reader[..ivSize].ToArray() : protection.ConstantIv;
            reader = reader[ivSize..];
            var subsamples = new List<MediaSubsample>();
            if (hasSubsamples)
            {
                if (reader.Length < 2)
                    return false;

                var count = BinaryPrimitives.ReadUInt16BigEndian(reader);
                reader = reader[2..];
                if (count > MaxSubsamples || reader.Length < count * 6)
                    return false;

                for (var i = 0; i < count; i++)
                {
                    subsamples.Add(new MediaSubsample(BinaryPrimitives.ReadUInt16BigEndian(reader), (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(reader[2..]), int.MaxValue)));
                    reader = reader[6..];
                }
            }

            information = (iv, subsamples);
            return true;
        }
    }
}
