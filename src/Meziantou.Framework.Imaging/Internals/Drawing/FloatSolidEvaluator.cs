namespace Meziantou.Framework.Imaging.Internals;

internal sealed class FloatSolidEvaluator(AllocationScope scope) : PaintEvaluator, IDisposable
{
    private PooledBuffer? _buffer;
    private RgbaFloat _color;

    public void Prepare(RgbaFloat color, int width)
    {
        _color = color;
        if (_buffer is null || _buffer.Span.Length < (long)width * 4 * sizeof(double))
        {
            _buffer?.Dispose();
            _buffer = ResampleWeights.Rent<double>(scope, 4L * width);
        }
    }

    public override ReadOnlySpan<ushort> Evaluate(int y, int x, int count) => throw new InvalidOperationException("A floating-point solid paint must be evaluated as floating-point samples.");

    public override bool TryEvaluateFloat(int y, int x, int count, double alphaFactor, out ReadOnlySpan<double> colors)
    {
        var samples = unsafe(System.Runtime.InteropServices.MemoryMarshal.Cast<byte, double>(_buffer!.Span))[..(count * 4)];
        for (var i = 0; i < samples.Length; i += 4)
        {
            samples[i] = _color.R;
            samples[i + 1] = _color.G;
            samples[i + 2] = _color.B;
            samples[i + 3] = Math.Clamp((double)_color.A, 0, 1) * alphaFactor;
        }

        colors = samples;
        return true;
    }

    public void Dispose()
    {
        _buffer?.Dispose();
        _buffer = null;
    }
}
