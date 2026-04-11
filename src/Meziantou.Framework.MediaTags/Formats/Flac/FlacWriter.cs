namespace Meziantou.Framework.MediaTags.Formats.Flac;

internal sealed class FlacWriter : IMediaTagWriter
{
    public MediaTagResult WriteTags(Stream inputStream, Stream outputStream, MediaTagInfo tags)
    {
        try
        {
            inputStream.Position = 0;

            // Copy "fLaC" magic
            Span<byte> magic = stackalloc byte[4];
            if (inputStream.ReadAtLeast(magic, 4, throwOnEndOfStream: false) < 4)
                return MediaTagResult.Failure(MediaTagError.CorruptFile, "File too small for FLAC.");

            outputStream.Write(magic);

            // Read all existing metadata blocks (preserve non-tag blocks)
            var preservedBlocks = new List<FlacMetadataBlock>();
            long audioDataStart = 4;
            Span<byte> blockHeader = stackalloc byte[4];

            while (true)
            {
                if (inputStream.ReadAtLeast(blockHeader, 4, throwOnEndOfStream: false) < 4)
                    break;

                var isLast = (blockHeader[0] & 0x80) != 0;
                var blockType = (byte)(blockHeader[0] & 0x7F);
                var blockSize = (blockHeader[1] << 16) | (blockHeader[2] << 8) | blockHeader[3];

                var blockData = new byte[blockSize];
                if (inputStream.ReadAtLeast(blockData, blockSize, throwOnEndOfStream: false) < blockSize)
                    break;

                audioDataStart = inputStream.Position;

                // Preserve everything except VORBIS_COMMENT, PICTURE, and PADDING
                if (blockType is not (FlacMetadataBlock.VorbisCommentType or FlacMetadataBlock.Picture or FlacMetadataBlock.Padding))
                {
                    preservedBlocks.Add(new FlacMetadataBlock
                    {
                        IsLast = false,
                        BlockType = blockType,
                        Data = blockData,
                    });
                }

                if (isLast)
                    break;
            }

            // Build new Vorbis Comment block (pictures are handled as FLAC PICTURE blocks, not in comments)
            var vorbisCommentData = VorbisComment.VorbisCommentWriter.Build(tags, includePictures: false);

            // Build new picture blocks
            var pictureBlocks = new List<byte[]>();
            foreach (var picture in tags.Pictures)
            {
                pictureBlocks.Add(FlacPictureBlock.Build(picture));
            }

            // Write preserved blocks
            foreach (var block in preservedBlocks)
            {
                WriteMetadataBlockHeader(outputStream, block.BlockType, block.Data.Length, isLast: false);
                outputStream.Write(block.Data);
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
            inputStream.Position = audioDataStart;
            inputStream.CopyTo(outputStream);

            return MediaTagResult.Success();
        }
        catch (Exception ex)
        {
            return MediaTagResult.Failure(MediaTagError.IoError, ex.Message);
        }
    }

    private static void WriteMetadataBlockHeader(Stream stream, byte blockType, int dataSize, bool isLast)
    {
        Span<byte> header = stackalloc byte[4];
        header[0] = (byte)((isLast ? 0x80 : 0x00) | (blockType & 0x7F));
        header[1] = (byte)((dataSize >> 16) & 0xFF);
        header[2] = (byte)((dataSize >> 8) & 0xFF);
        header[3] = (byte)(dataSize & 0xFF);
        stream.Write(header);
    }
}
