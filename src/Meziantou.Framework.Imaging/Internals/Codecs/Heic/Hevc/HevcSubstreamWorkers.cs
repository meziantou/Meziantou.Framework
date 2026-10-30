namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The working memory of the workers that decode the substreams of a slice segment concurrently (the tiles, and the rows
/// of coding tree blocks of the wavefront, that start at an entry point): an arithmetic decoder and the buffers of a
/// transform block for each worker. The decoder keeps it for its pictures; it is rented on the calling thread, before the
/// workers start.
/// </summary>
internal sealed class HevcSubstreamWorkers : IDisposable
{
    private readonly AllocationScope _scope;
    private readonly List<HevcCabacDecoder> _cabac = [];
    private readonly List<HevcSliceScratch> _scratch = [];

    public HevcSubstreamWorkers(AllocationScope scope)
    {
        _scope = scope;
    }

    /// <summary>Gets the allocation scope that the memory of the workers is charged to.</summary>
    public AllocationScope Scope => _scope;

    /// <summary>Gets the arithmetic decoder of a worker.</summary>
    public HevcCabacDecoder GetCabac(int worker) => _cabac[worker];

    /// <summary>Gets the buffers of a worker.</summary>
    public HevcSliceScratch GetScratch(int worker) => _scratch[worker];

    /// <summary>Rents the memory of <paramref name="workers"/> workers, with the buffers of the inter prediction when <paramref name="inter"/>.</summary>
    /// <exception cref="ImageResourceLimitException">The buffers exceed the live-allocation limit.</exception>
    public void Prepare(int workers, bool inter)
    {
        while (_scratch.Count < workers)
        {
            _scratch.Add(new HevcSliceScratch(_scope));
            _cabac.Add(new HevcCabacDecoder());
        }

        if (inter)
        {
            for (var i = 0; i < workers; i++)
            {
                _scratch[i].RentInterBuffers();
            }
        }
    }

    public void Dispose()
    {
        foreach (var scratch in _scratch)
        {
            scratch.Dispose();
        }

        _scratch.Clear();
        _cabac.Clear();
    }
}
