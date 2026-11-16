namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The parts of a KTX 2.0 file in order, and the charges of the buffers the writer created for them.</summary>
internal sealed class Ktx2FileParts : IDisposable
{
    private readonly List<AllocationCharge> _charges = [];

    public List<ReadOnlyMemory<byte>> Parts { get; } = [];

    public void Add(ReadOnlyMemory<byte> part) => Parts.Add(part);

    public void AddCharge(AllocationCharge charge) => _charges.Add(charge);

    public void Dispose()
    {
        foreach (var charge in _charges)
        {
            charge.Dispose();
        }

        _charges.Clear();
        Parts.Clear();
    }
}
