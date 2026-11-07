namespace Sample;

public sealed class Arithmetic
{
    public void M(
        [ValueTag("OrderNumber")] int orderNumber,
        [ValueTag("ProjectNumber")] int projectNumber,
        [ValueTag("Meters")] double meters,
        [ValueTag("Seconds")] double seconds)
    {
        #region TaggedValues_Arithmetic
        _ = orderNumber + 1 == projectNumber; // MFTV0001, orderNumber + 1 is still an OrderNumber
        _ = orderNumber + projectNumber;      // MFTV0003

        var doubled = meters * 2;             // still in Meters
        _ = doubled == seconds;               // MFTV0001

        var speed = meters / seconds;         // not tagged
        _ = speed == seconds;                 // ok
        #endregion
    }
}
