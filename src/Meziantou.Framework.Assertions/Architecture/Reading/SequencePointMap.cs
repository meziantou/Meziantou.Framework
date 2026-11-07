using System.Reflection.Metadata;

namespace Meziantou.Framework.Assertions.Architecture.Reading;

// Maps the IL offsets of a method to the statements of the source code, using the portable PDB
internal sealed class SequencePointMap
{
    private readonly (int Offset, SourceLocation Location)[] _points;

    private SequencePointMap((int Offset, SourceLocation Location)[] points)
    {
        _points = points;
    }

    public SourceLocation? First => _points.Length > 0 ? _points[0].Location : null;

    public static SequencePointMap? Create(AssemblyImage image, MethodDefinitionHandle method)
    {
        if (image.PdbReader is not { } pdbReader)
            return null;

        try
        {
            var points = new List<(int Offset, SourceLocation Location)>();
            foreach (var sequencePoint in pdbReader.GetMethodDebugInformation(method).GetSequencePoints())
            {
                if (sequencePoint.IsHidden || sequencePoint.Document.IsNil)
                    continue;

                points.Add((sequencePoint.Offset, new SourceLocation(image.GetDocumentPath(pdbReader, sequencePoint.Document), sequencePoint.StartLine, sequencePoint.StartColumn)));
            }

            if (points.Count == 0)
                return null;

            points.Sort((a, b) => a.Offset.CompareTo(b.Offset));
            return new SequencePointMap([.. points]);
        }
        catch (BadImageFormatException)
        {
            return null;
        }
    }

    // Returns the statement that contains the instruction at the offset: the last sequence point that starts at or before it
    public SourceLocation? Find(int offset)
    {
        var low = 0;
        var high = _points.Length - 1;
        var result = -1;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            if (_points[middle].Offset <= offset)
            {
                result = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return result >= 0 ? _points[result].Location : First;
    }
}
