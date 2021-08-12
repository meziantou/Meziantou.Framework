using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Meziantou.Framework.DependencyScanning.Locations;

internal sealed class NonUpdatableLocation : Location
{
    public NonUpdatableLocation(string filePath)
        : base(filePath)
    {
    }
    public override bool IsUpdatable => false;

    protected internal override Task UpdateAsync(Stream stream, string newVersion, CancellationToken cancellationToken)
    {
        throw new InvalidOperationException("This dependency cannot be updated");
    }
}
