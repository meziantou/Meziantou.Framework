using System.Diagnostics;
using Meziantou.Framework.MediaTags.Internals;

namespace Meziantou.Framework.MediaTags.Formats.Flac;

internal sealed class FlacWriter : IMediaTagWriter
{
    public MediaTagResult WriteTags(Stream inputStream, Stream outputStream, MediaTagInfo tags, MediaTagWriteOptions options)
    {
        try
        {
            inputStream.Position = 0;

            // The FLAC signature is not always at the start of the file: some taggers prepend an ID3v2
            // tag, and FlacReader reads those files. Writing from offset 0 regardless would copy the tag
            // header in place of the signature and parse the tag as metadata blocks, destroying the audio.
            if (!FlacStreamLocator.TryGetStreamStart(inputStream, out var streamStart))
                return MediaTagResult.Failure(MediaTagError.CorruptFile, "Not a FLAC file.");

            // Copy anything preceding the signature verbatim
            inputStream.Position = 0;
            if (!StreamHelpers.CopyExactly(inputStream, outputStream, streamStart))
                return MediaTagResult.Failure(MediaTagError.UnexpectedEndOfStream, "The file ended before the FLAC signature.");

            // Copy "fLaC" magic
            Span<byte> magic = stackalloc byte[4];
            if (inputStream.ReadAtLeast(magic, 4, throwOnEndOfStream: false) < 4)
                return MediaTagResult.Failure(MediaTagError.UnexpectedEndOfStream, "File too small for FLAC.");

            outputStream.Write(magic);

            var preservedBlocks = new List<FlacMetadataBlock>();
            if (!TryReadMetadataBlocks(inputStream, preservedBlocks, out var audioDataStart, out var readError))
                return readError;

            // Build new Vorbis Comment block (pictures are handled as FLAC PICTURE blocks, not in comments)
            var vorbisCommentData = VorbisComment.VorbisCommentWriter.Build(tags, includePictures: false);
            if (vorbisCommentData.Length > FlacMetadataBlock.MaxSize)
                return MediaTagResult.Failure(MediaTagError.InvalidTagData, $"The tags are too large for a FLAC metadata block ({vorbisCommentData.Length} bytes, the maximum is {FlacMetadataBlock.MaxSize}).");

            // Build new picture blocks
            var pictureBlocks = new List<byte[]>();
            foreach (var picture in tags.Pictures)
            {
                var pictureBlock = FlacPictureBlock.Build(picture);

                // A metadata block header only has 24 bits for the size. Writing a larger block would truncate
                // the declared size, and every reader would then walk the block chain into the picture bytes.
                if (pictureBlock.Length > FlacMetadataBlock.MaxSize)
                    return MediaTagResult.Failure(MediaTagError.InvalidTagData, $"A picture is too large for a FLAC metadata block ({pictureBlock.Length} bytes, the maximum is {FlacMetadataBlock.MaxSize}).");

                pictureBlocks.Add(pictureBlock);
            }

            // Write preserved blocks, streaming their content straight from the input
            foreach (var block in preservedBlocks)
            {
                WriteMetadataBlockHeader(outputStream, block.BlockType, block.Size, isLast: false);
                if (!StreamHelpers.CopyExactlyFrom(inputStream, outputStream, block.Position, block.Size))
                    return MediaTagResult.Failure(MediaTagError.UnexpectedEndOfStream, "The file ended inside a FLAC metadata block.");
            }

            // Write Vorbis Comment block
            var isVorbisLast = pictureBlocks.Count == 0;
            WriteMetadataBlockHeader(outputStream, FlacMetadataBlock.VorbisCommentType, vorbisCommentData.Length, isLast: isVorbisLast);
            outputStream.Write(vorbisCommentData);

            // Write picture blocks
            for (var i = 0; i < pictureBlocks.Count; i++)
            {
                var isLastPicture = i == pictureBlocks.Count - 1;
                WriteMetadataBlockHeader(outputStream, FlacMetadataBlock.Picture, pictureBlocks[i].Length, isLast: isLastPicture);
                outputStream.Write(pictureBlocks[i]);
            }

            // Copy audio data
            if (!StreamHelpers.CopyExactlyFrom(inputStream, outputStream, audioDataStart, inputStream.Length - audioDataStart))
                return MediaTagResult.Failure(MediaTagError.UnexpectedEndOfStream, "The file ended before all audio data could be read.");

            return MediaTagResult.Success();
        }
        catch (Exception ex) when (MediaTagErrors.TryMap(ex, out var error))
        {
            return MediaTagResult.Failure(error, ex.Message);
        }
    }

    /// <summary>
    /// Records the metadata blocks to preserve and the offset the audio starts at.
    /// </summary>
    /// <remarks>
    /// Every block must be accounted for. Stopping early and copying from wherever the parse gave up would
    /// write the unparsed bytes into the audio region of a file that then replaces the caller's.
    /// </remarks>
    private static bool TryReadMetadataBlocks(Stream inputStream, List<FlacMetadataBlock> preservedBlocks, out long audioDataStart, out MediaTagResult error)
    {
        audioDataStart = 0;
        error = default;

        Span<byte> blockHeader = stackalloc byte[4];
        while (true)
        {
            if (inputStream.ReadAtLeast(blockHeader, 4, throwOnEndOfStream: false) < 4)
            {
                error = MediaTagResult.Failure(MediaTagError.UnexpectedEndOfStream, "The file ended inside the FLAC metadata.");
                return false;
            }

            var isLast = (blockHeader[0] & 0x80) != 0;
            var blockType = (byte)(blockHeader[0] & 0x7F);
            var blockSize = (blockHeader[1] << 16) | (blockHeader[2] << 8) | blockHeader[3];
            var blockPosition = inputStream.Position;

            if (blockSize > inputStream.Length - blockPosition)
            {
                error = MediaTagResult.Failure(MediaTagError.CorruptFile, "A FLAC metadata block runs past the end of the file.");
                return false;
            }

            // Preserve everything except VORBIS_COMMENT, PICTURE, and PADDING
            if (blockType is not (FlacMetadataBlock.VorbisCommentType or FlacMetadataBlock.Picture or FlacMetadataBlock.Padding))
            {
                if (preservedBlocks.Count >= FlacMetadataBlock.MaxCount)
                {
                    error = MediaTagResult.Failure(MediaTagError.CorruptFile, $"The file declares more than {FlacMetadataBlock.MaxCount} FLAC metadata blocks.");
                    return false;
                }

                preservedBlocks.Add(new FlacMetadataBlock
                {
                    BlockType = blockType,
                    Position = blockPosition,
                    Size = blockSize,
                });
            }

            inputStream.Position = blockPosition + blockSize;

            if (isLast)
            {
                audioDataStart = inputStream.Position;
                return true;
            }
        }
    }

    private static void WriteMetadataBlockHeader(Stream stream, byte blockType, int dataSize, bool isLast)
    {
        Debug.Assert(dataSize is >= 0 and <= FlacMetadataBlock.MaxSize, "The block size must be validated before the header is written.");

        Span<byte> header = stackalloc byte[4];
        header[0] = (byte)((isLast ? 0x80 : 0x00) | (blockType & 0x7F));
        header[1] = (byte)((dataSize >> 16) & 0xFF);
        header[2] = (byte)((dataSize >> 8) & 0xFF);
        header[3] = (byte)(dataSize & 0xFF);
        stream.Write(header);
    }
}
