using SampleApp.Modules.Shipping;

namespace SampleApp.Modules.Billing;

public sealed class Invoice
{
    public Shipment? Shipment { get; set; }
}
