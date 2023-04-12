using System.Collections.ObjectModel;
using Meziantou.Framework.InlineSnapshotTesting.Serialization;
using Xunit;

namespace Meziantou.Framework.InlineSnapshotTesting.Tests;
public sealed class SnapshotSerializerTests
{
    private static readonly InlineSnapshotSettings Settings = InlineSnapshotSettings.Default with
    {
        ValidateLineNumberUsingPdbInfoWhenAvailable = false, // Fail on net472
        MergeTool = DiffEngine.DiffTool.VisualStudioCode,
    };

    [Fact]
    public void Argon()
    {
        InlineSnapshot.Validate(new ArgonSnapshotSerializer().Serialize(new Sample()), Settings, """
            {
                Int32: 42,
                NullableInt32: null,
                NullableInt32_NotNull: 42,
                Int32Array: [
                    1,
                    2,
                    3,
                    4,
                    5
                ],
                EmptyArray: [],
                NullArray: null,
                IEnumerableInt32: [
                    0,
                    1
                ],
                IDictionary: {
                    1: 2,
                    3: 4
                },
                IReadOnlyDictionary: {
                    1: 2,
                    3: 4
                },
                Enum: 2,
                Enum_NotDefined: 100,
                FlagsEnum: 7,
                FlagsEnum_NotDefined: 35,
                DateTime_Utc: 2000-01-01T01:01:01Z,
                DateTime_Unspecified: 2000-01-01T01:01:01,
                NullableDateTime: null,
                DateTimeOffset_Zero: 2000-01-01T01:01:01+00:00,
                DateTimeOffset_NonZero: 2000-01-01T01:01:01+02:00,
                NullableDateTimeOffset: null,
                NullableDateTimeOffset_NotNull: 2000-01-01T01:01:01+00:00,
                Guid: 4871547b-835b-4c06-ab0e-10931af0cd8d,
                NestedObject: {
                    StringValueStartingWithExclamationMark: !1,
                    StringValue: Dummy,
                    MultiLineStringValue: Line1
            Line2
            Line3,
                    NullableEnum: null,
                    StringValue1: Constant,
                    StringValue2: Constant
                }
            }
            """);
    }

    [Fact]
    public void Json()
    {
        InlineSnapshot.Validate(new JsonSnapshotSerializer().Serialize(new Sample()), Settings, """
            {
              "Int32": 42,
              "NullableInt32": null,
              "NullableInt32_NotNull": 42,
              "Int32Array": [
                1,
                2,
                3,
                4,
                5
              ],
              "EmptyArray": [],
              "NullArray": null,
              "IEnumerableInt32": [
                0,
                1
              ],
              "IDictionary": {
                "1": 2,
                "3": 4
              },
              "IReadOnlyDictionary": {
                "1": 2,
                "3": 4
              },
              "Enum": 2,
              "Enum_NotDefined": 100,
              "FlagsEnum": 7,
              "FlagsEnum_NotDefined": 35,
              "DateTime_Utc": "2000-01-01T01:01:01Z",
              "DateTime_Unspecified": "2000-01-01T01:01:01",
              "NullableDateTime": null,
              "DateTimeOffset_Zero": "2000-01-01T01:01:01+00:00",
              "DateTimeOffset_NonZero": "2000-01-01T01:01:01+02:00",
              "NullableDateTimeOffset": null,
              "NullableDateTimeOffset_NotNull": "2000-01-01T01:01:01+00:00",
              "Guid": "4871547b-835b-4c06-ab0e-10931af0cd8d",
              "NestedObject": {
                "StringValueStartingWithExclamationMark": "!1",
                "StringValue": "Dummy",
                "MultiLineStringValue": "Line1\nLine2\nLine3",
                "NullableEnum": null,
                "StringValue1": "Constant",
                "StringValue2": "Constant"
              }
            }
            """);
    }

    [Fact]
    public void Readable()
    {
        InlineSnapshot.Validate(HumanReadableSnapshotSerializer.Instance.Serialize(new Sample()), Settings, """
            Int32: 42
            NullableDateTimeOffset_NotNull: 2000-01-01T01:01:01+00:00
            NullableDateTimeOffset: <null>
            DateTimeOffset_NonZero: 2000-01-01T01:01:01+02:00
            DateTimeOffset_Zero: 2000-01-01T01:01:01+00:00
            NullableDateTime: <null>
            DateTime_Unspecified: 2000-01-01T01:01:01
            DateTime_Utc: 2000-01-01T01:01:01Z
            FlagsEnum_NotDefined: 35
            FlagsEnum: ReadWrite, Delete
            Enum_NotDefined: 100
            Enum: Tuesday
            IReadOnlyDictionary:
              - Key: 1
                Value: 2
              - Key: 3
                Value: 4
            IDictionary:
              - Key: 1
                Value: 2
              - Key: 3
                Value: 4
            IEnumerableInt32:
              - 0
              - 1
            NullArray: <null>
            EmptyArray: []
            Int32Array:
              - 1
              - 2
              - 3
              - 4
              - 5
            NullableInt32_NotNull: 42
            NullableInt32: <null>
            Guid: 4871547b-835b-4c06-ab0e-10931af0cd8d
            NestedObject:
              StringValueStartingWithExclamationMark: !1
              StringValue: Dummy
              MultiLineStringValue:
                Line1
                Line2
                Line3
              NullableEnum: <null>
              StringValue1: Constant
              StringValue2: Constant
            """);

        InlineSnapshot.Validate(HumanReadableSnapshotSerializer.Instance.Serialize(null), Settings, "<null>");

        InlineSnapshot.Validate(HumanReadableSnapshotSerializer.Instance.Serialize(new { A = 1, B = 2 }), Settings, """
            A: 1
            B: 2
            """);
    }

    private sealed class Sample
    {
        public int Int32 { get; set; } = 42;
        public int? NullableInt32 { get; set; }
        public int? NullableInt32_NotNull { get; set; } = 42;
        public int[] Int32Array { get; set; } = new int[] { 1, 2, 3, 4, 5 };
        public int[] EmptyArray { get; set; } = Array.Empty<int>();
        public int[] NullArray { get; set; }
        public IEnumerable<int> IEnumerableInt32 { get; set; } = Enumerable.Range(0, 2);
        public IDictionary<int, int> IDictionary { get; set; } = new Dictionary<int, int>() { [1] = 2, [3] = 4 };
        public IReadOnlyDictionary<int, int> IReadOnlyDictionary { get; set; } = new ReadOnlyDictionary<int, int>(new Dictionary<int, int>() { [1] = 2, [3] = 4 });
        public DayOfWeek Enum { get; set; } = DayOfWeek.Tuesday;
        public DayOfWeek Enum_NotDefined { get; set; } = (DayOfWeek)100;
        public FileShare FlagsEnum { get; set; } = FileShare.Read | FileShare.Write | FileShare.Delete;
        public FileShare FlagsEnum_NotDefined { get; set; } = FileShare.Read | FileShare.Write | (FileShare)0x20;
        public DateTime DateTime_Utc { get; set; } = new DateTime(2000, 1, 1, 1, 1, 1, DateTimeKind.Utc);
        public DateTime DateTime_Unspecified { get; set; } = new DateTime(2000, 1, 1, 1, 1, 1, DateTimeKind.Unspecified);
        public DateTime? NullableDateTime { get; set; }
        public DateTimeOffset DateTimeOffset_Zero { get; set; } = new DateTimeOffset(2000, 1, 1, 1, 1, 1, TimeSpan.Zero);
        public DateTimeOffset DateTimeOffset_NonZero { get; set; } = new DateTimeOffset(2000, 1, 1, 1, 1, 1, TimeSpan.FromHours(2));
        public DateTimeOffset? NullableDateTimeOffset { get; set; }
        public DateTimeOffset? NullableDateTimeOffset_NotNull { get; set; } = new DateTimeOffset(2000, 1, 1, 1, 1, 1, TimeSpan.Zero);
        public Guid Guid { get; set; } = new Guid("4871547b-835b-4c06-ab0e-10931af0cd8d");
        public NestedSample NestedObject { get; set; } = new NestedSample();
    }

    private sealed class NestedSample
    {
        private const string Value = "Constant";

        public string StringValueStartingWithExclamationMark { get; set; } = "!1";
        public string StringValue { get; set; } = "Dummy";
        public string MultiLineStringValue { get; set; } = "Line1\nLine2\nLine3";
        public DayOfWeek? NullableEnum { get; set; }

        public string StringValue1 { get; set; } = Value;
        public string StringValue2 { get; set; } = Value;
    }
}
