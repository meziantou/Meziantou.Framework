using System.Collections.ObjectModel;
using System.Net;
using System.Net.Http.Headers;
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
    public void HumanReadable()
    {
        InlineSnapshot.Validate(HumanReadableSnapshotSerializer.Instance.Serialize(new Sample()), Settings, """
            DateTimeOffset_NonZero: 2000-01-01T01:01:01+02:00
            DateTimeOffset_Zero: 2000-01-01T01:01:01+00:00
            DateTime_Unspecified: 2000-01-01T01:01:01
            DateTime_Utc: 2000-01-01T01:01:01Z
            EmptyArray: []
            Enum: Tuesday
            Enum_NotDefined: 100
            FlagsEnum: ReadWrite, Delete
            FlagsEnum_NotDefined: 35
            Guid: 4871547b-835b-4c06-ab0e-10931af0cd8d
            IDictionary:
              - Key: 1
                Value: 2
              - Key: 3
                Value: 4
            IEnumerableInt32:
              - 0
              - 1
            IReadOnlyDictionary:
              - Key: 1
                Value: 2
              - Key: 3
                Value: 4
            Int32: 42
            Int32Array:
              - 1
              - 2
              - 3
              - 4
              - 5
            NestedObject:
              MultiLineStringValue:
                Line1
                Line2
                Line3
              StringValue: Dummy
              StringValue1: Constant
              StringValue2: Constant
              StringValueStartingWithExclamationMark: !1
            NullableDateTimeOffset_NotNull: 2000-01-01T01:01:01+00:00
            NullableInt32_NotNull: 42
            """);

        InlineSnapshot.Validate(HumanReadableSnapshotSerializer.Instance.Serialize(null), Settings, "<null>");

        InlineSnapshot.Validate(HumanReadableSnapshotSerializer.Instance.Serialize(new { A = 1, B = 2 }), Settings, """
            A: 1
            B: 2
            """);
    }

    [Fact]
    public void HumanReadable_HttpRequestMessage_Method_Uri()
    {
        using var message = new HttpRequestMessage() { RequestUri = new Uri("https://example.com") };
        InlineSnapshot.Validate(HumanReadableSnapshotSerializer.Instance.Serialize(message), Settings, """
            Method: GET
            RequestUri: https://example.com/
            """);
    }
    [Fact]
    public void HumanReadable_HttpRequestMessage_Method_Uri_Version()
    {
        using var message = new HttpRequestMessage() { RequestUri = new Uri("https://example.com"), Version = HttpVersion.Version10 };
        InlineSnapshot.Validate(HumanReadableSnapshotSerializer.Instance.Serialize(message), Settings, """
            Method: GET
            RequestUri: https://example.com/
            Version: 1.0
            """);
    }
    
    [Fact]
    public void HumanReadable_HttpRequestMessage_Method_Uri_Headers()
    {
        using var message = new HttpRequestMessage()
        {
            RequestUri = new Uri("https://example.com"),
            Headers =
            {
                Accept =
                {
                    new MediaTypeWithQualityHeaderValue("text/json"),
                    new MediaTypeWithQualityHeaderValue("text/html"),
                },
            },
        };
        InlineSnapshot.Validate(HumanReadableSnapshotSerializer.Instance.Serialize(message), Settings, """
            Method: GET
            RequestUri: https://example.com/
            Headers:
              Accept:
                - text/json
                - text/html
            """);
    }
    
    [Fact]
    public void HumanReadable_HttpRequestMessage_Method_Uri_Content()
    {
        using var message = new HttpRequestMessage()
        {
            RequestUri = new Uri("https://example.com"),
            Content = new StringContent("foo"),
        };
        InlineSnapshot.Validate(HumanReadableSnapshotSerializer.Instance.Serialize(message), Settings, """
            Method: GET
            RequestUri: https://example.com/
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
              Content: foo
            """);
    }

    [Fact]
    public void HumanReadable_HttpResponseMessage()
    {
        using var message = new HttpResponseMessage()
        {
            StatusCode = HttpStatusCode.OK,
            Headers =
            {
                ETag = new EntityTagHeaderValue("\"dummy\""),
            },
            Content = new ByteArrayContent(Array.Empty<byte>()),
        };
        InlineSnapshot.Validate(HumanReadableSnapshotSerializer.Instance.Serialize(message), Settings, """
            StatusCode: 200 (OK)
            Headers:
              ETag: "dummy"
            Content:
            """);
    }

#if NET5_0_OR_GREATER
    [Fact]
    public void HumanReadable_HttpResponseMessage_TrailingHeaders()
    {
        using var message = new HttpResponseMessage()
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent("foo"),
            Headers =
            {
                ETag = new EntityTagHeaderValue("\"dummy\""),
            },
            TrailingHeaders =
            {
                ETag = new EntityTagHeaderValue("\"dummy\""),
            },
        };
        InlineSnapshot.Validate(HumanReadableSnapshotSerializer.Instance.Serialize(message), Settings, """
            StatusCode: 200 (OK)
            Headers:
              ETag: "dummy"
            TrailingHeaders:
              ETag: "dummy"
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
              Content: foo
            """);
    }
#endif

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
