using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>Sends the elements of a shape to the sink of a draw call.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly struct SinkPathReceiver(PathSink sink) : IPathReceiver
{
    public void MoveTo(double x, double y) => sink.MoveTo(x, y);

    public void LineTo(double x, double y) => sink.LineTo(x, y, smooth: false);

    public void Arc(scoped in ArcPiece piece) => PathFlattener.Arc(sink, piece);

    public void Close() => sink.Close(smooth: false);
}
