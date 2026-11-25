using System.Globalization;
using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Fuzz;

// The report descriptors of WebHID (https://hid.spec.whatwg.org/#dfn-parse-the-report-descriptor): the bytes of a device,
// which the library parses into the collections of HIDDevice
public static partial class WebPlatformFuzzTargets
{
    /// <summary>
    /// Parses a report descriptor, and checks the properties of the collections: the parser does not throw, the collections
    /// are at most 32 levels deep, a report of a collection is also a report of its top-level collection (where the blocklist
    /// finds it), the report IDs are 0 when the descriptor has no Report ID item, a range has its minimum before its maximum,
    /// and an item has at most 256 strings.
    /// </summary>
    public static void HidReportDescriptorTarget(ReadOnlySpan<byte> data)
    {
        var descriptor = HidReportDescriptor.Parse(data, index => index % 7 == 0 ? null : "String " + index.ToString(CultureInfo.InvariantCulture));
        foreach (var collection in descriptor.Collections)
        {
            CheckHidCollection(collection, collection, descriptor, depth: 1);
        }

        // The blocklist checks every report of the device
        var device = new FuzzHidDevice(data.ToArray());
        foreach (var (type, reportId) in descriptor.ReportCollections.Keys)
        {
            _ = descriptor.IsBlocked(device, type, reportId);
        }
    }

    private static void CheckHidCollection(HidCollectionInfo collection, HidCollectionInfo topLevel, HidReportDescriptor descriptor, int depth)
    {
        FuzzAssert.True(depth <= 32, "A collection is more than 32 levels deep");
        foreach (var (reports, type) in new[] { (collection.InputReports!, HidReportType.Input), (collection.OutputReports!, HidReportType.Output), (collection.FeatureReports!, HidReportType.Feature) })
        {
            foreach (var report in reports)
            {
                FuzzAssert.True(descriptor.UsesReportIds || report.ReportId == 0, "A report has an ID, but the descriptor has no Report ID item");
                FuzzAssert.True(descriptor.ReportCollections.TryGetValue((type, report.ReportId!.Value), out var containing) && containing.Contains(topLevel), "A report is not a report of its top-level collection");
                FuzzAssert.Equal(1, reports.Count(r => r.ReportId == report.ReportId), "A collection has two reports with the same ID");
                foreach (var item in report.Items!)
                {
                    FuzzAssert.True(item.IsRange == (item.UsageMinimum is not null && item.UsageMaximum is not null), "isRange does not tell whether the usage range is present");
                    FuzzAssert.True(item.IsRange is not true || item.UsageMinimum <= item.UsageMaximum, "The usage range is reversed");
                    FuzzAssert.True(item.Strings!.Count <= 256, "An item has more than 256 strings");
                }
            }
        }

        foreach (var child in collection.Children!)
        {
            CheckHidCollection(child, topLevel, descriptor, depth + 1);
        }
    }

    private static IEnumerable<FuzzSeed> GetHidReportDescriptorSeeds()
    {
        byte[][] seeds =
        [
            // A vendor-defined device with report IDs (input, output and feature reports)
            [0x06, 0x00, 0xFF, 0x09, 0x01, 0xA1, 0x01, 0x85, 0x01, 0x15, 0x00, 0x26, 0xFF, 0x00, 0x75, 0x08, 0x95, 0x08, 0x09, 0x02, 0x81, 0x02, 0x09, 0x03, 0x91, 0x02, 0x85, 0x02, 0x95, 0x04, 0x09, 0x04, 0xB1, 0x02, 0xC0],

            // A keyboard (blocked) without report IDs
            [0x05, 0x01, 0x09, 0x06, 0xA1, 0x01, 0x75, 0x08, 0x95, 0x01, 0x91, 0x02, 0xC0],

            // A mouse: a nested physical collection, a usage range, padding and signed logical limits
            [0x05, 0x01, 0x09, 0x02, 0xA1, 0x01, 0x09, 0x01, 0xA1, 0x00, 0x05, 0x09, 0x19, 0x01, 0x29, 0x03, 0x15, 0x00, 0x25, 0x01, 0x95, 0x03, 0x75, 0x01, 0x81, 0x02, 0x95, 0x01, 0x75, 0x05, 0x81, 0x03, 0x05, 0x01, 0x09, 0x30, 0x09, 0x31, 0x15, 0x81, 0x25, 0x7F, 0x75, 0x08, 0x95, 0x02, 0x81, 0x06, 0xC0, 0xC0],

            // Push and Pop, a unit, a unit exponent, 4-byte usages, string indices and a long item
            [0x05, 0x0D, 0x09, 0x04, 0xA1, 0x01, 0xA4, 0x05, 0x01, 0x66, 0x11, 0x00, 0x55, 0x0E, 0x0B, 0x30, 0x00, 0x01, 0x00, 0x79, 0x01, 0x89, 0x02, 0x99, 0x04, 0x35, 0x00, 0x47, 0xFF, 0xFF, 0x00, 0x00, 0x81, 0x02, 0xB4, 0xFE, 0x02, 0x10, 0xAA, 0xBB, 0xC0],

            // Unbalanced collections, a truncated item, and items outside of any collection
            [0x81, 0x02, 0xC0, 0xC0, 0xA1, 0x01, 0xA1, 0x02, 0x85, 0x00, 0x91, 0x02, 0xB4, 0xB4, 0x27, 0xFF],
            [],
        ];

        for (var i = 0; i < seeds.Length; i++)
        {
            yield return new FuzzSeed("hid-report-descriptor-" + i.ToString(CultureInfo.InvariantCulture), seeds[i]);
        }
    }

    private sealed class FuzzHidDevice(byte[] descriptor) : HidHostDevice(0x0b0e, 0x60fc, "Fuzz", descriptor)
    {
        public override Task<HidDeviceSession> OpenAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
