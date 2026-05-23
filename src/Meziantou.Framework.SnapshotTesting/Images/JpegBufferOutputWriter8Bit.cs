using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using JpegLibrary;

namespace Meziantou.Framework.SnapshotTesting;

internal sealed class JpegBufferOutputWriter8Bit(int width, int height, int componentCount, Memory<byte> output) : JpegBlockOutputWriter
{
    private readonly int _width = width;
    private readonly int _height = height;
    private readonly int _componentCount = componentCount;
    private readonly Memory<byte> _output = output;

    public override void WriteBlock(ref short blockRef, int componentIndex, int x, int y)
    {
        if (x > _width || y > _height)
            return;

        var writeWidth = Math.Min(_width - x, 8);
        var writeHeight = Math.Min(_height - y, 8);

        ref var destinationRef = ref MemoryMarshal.GetReference(_output.Span);
        destinationRef = ref Unsafe.Add(ref destinationRef, y * _width * _componentCount + x * _componentCount + componentIndex);

        for (var destinationY = 0; destinationY < writeHeight; destinationY++)
        {
            ref var destinationRowRef = ref Unsafe.Add(ref destinationRef, destinationY * _width * _componentCount);
            for (var destinationX = 0; destinationX < writeWidth; destinationX++)
            {
                Unsafe.Add(ref destinationRowRef, destinationX * _componentCount) = ClampTo8Bit(Unsafe.Add(ref blockRef, destinationX));
            }

            blockRef = ref Unsafe.Add(ref blockRef, 8);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte ClampTo8Bit(short value)
    {
        return (byte)Math.Clamp(value, (short)0, (short)255);
    }
}
