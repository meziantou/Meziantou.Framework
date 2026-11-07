using SampleApp.Modules.Billing;

namespace SampleApp.Modules.Shipping;

public sealed class Shipment
{
    public Invoice? Invoice { get; set; }
}
