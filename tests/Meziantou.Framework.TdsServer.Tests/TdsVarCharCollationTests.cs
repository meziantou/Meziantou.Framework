using Meziantou.Xunit;

namespace Meziantou.Framework.Tds.Tests;

/// <summary>Covers the code page a <c>varchar</c> parameter is decoded with.</summary>
/// <remarks>
/// These tests drive the protocol directly instead of going through SqlClient, so unlike
/// <see cref="TdsServerProtocolTests"/> they do not need a real culture and run in invariant globalization mode
/// too. That matters here: the collations whose code page comes from a sort id are the ones that have to keep
/// working when no culture data is available.
/// </remarks>
public sealed class TdsVarCharCollationTests
{
    [Fact, RunIf(globalizationMode: TestGlobalizationMode.NotInvariant)]
    public async Task RpcParameter_VarChar_WithAWindowsCollation_UsesTheLocaleCodePage()
    {
        // Latin1_General_CI_AS: LCID 0x0409 and no sort id, so the code page comes from the locale.
        var context = await RawTdsClient.SendRpcRequestAsync(CreateVarCharRpcPayload([0x09, 0x04, 0xD0, 0x00, 0x00], [0x63, 0x61, 0x66, 0xE9]));

        Assert.True(context.HasCompleteParameters);
        var parameter = Assert.Single(context.Parameters);
        Assert.Equal("café", parameter.AsString());
    }

    [Fact, RunIf(globalizationMode: TestGlobalizationMode.Invariant)]
    public async Task RpcParameter_VarChar_WithAWindowsCollation_InInvariantMode_ReportsIncompleteParameters()
    {
        // Invariant globalization resolves every locale id to the invariant culture, so the code page it reports
        // says nothing about the collation. Answering CP1252 for a Japanese collation would lose the value
        // silently, so the parameter is rejected instead.
        var context = await RawTdsClient.SendRpcRequestAsync(CreateVarCharRpcPayload([0x09, 0x04, 0xD0, 0x00, 0x00], [0x63, 0x61, 0x66, 0xE9]));

        Assert.False(context.HasCompleteParameters);
        Assert.Empty(context.Parameters);
    }

    [Fact]
    public async Task RpcParameter_VarChar_WithAJapaneseCollation_UsesTheSortIdCodePage()
    {
        // Japanese_CI_AS: sort id 192 names code page 932, which is nothing like the Latin code pages.
        var context = await RawTdsClient.SendRpcRequestAsync(CreateVarCharRpcPayload([0x11, 0x04, 0xD0, 0x00, 192], [0x93, 0xFA, 0x96, 0x7B]));

        Assert.True(context.HasCompleteParameters);
        var parameter = Assert.Single(context.Parameters);
        Assert.Equal("日本", parameter.AsString());
    }

    [Fact]
    public async Task RpcParameter_VarChar_WithAUtf8Collation_IsDecodedAsUtf8()
    {
        // A UTF-8 collation keeps the LCID of the Windows collation it derives from, so only the flag at bit 26
        // tells the two apart.
        var context = await RawTdsClient.SendRpcRequestAsync(CreateVarCharRpcPayload([0x09, 0x04, 0xD0, 0x04, 0x00], [0x63, 0x61, 0x66, 0xC3, 0xA9]));

        Assert.True(context.HasCompleteParameters);
        var parameter = Assert.Single(context.Parameters);
        Assert.Equal("café", parameter.AsString());
    }

    [Fact]
    public async Task RpcParameter_VarChar_WithAnUnknownCollation_ReportsIncompleteParameters()
    {
        // Decoding with an arbitrary code page would replace every byte the encoding cannot map, which loses the
        // value without saying so. An unknown collation has to take the same path as an unknown type.
        var context = await RawTdsClient.SendRpcRequestAsync(CreateVarCharRpcPayload([0x09, 0x04, 0xD0, 0x00, 0xFF], [0x63, 0x61, 0x66, 0xE9]));

        Assert.False(context.HasCompleteParameters);
        Assert.Empty(context.Parameters);
    }

    private static byte[] CreateVarCharRpcPayload(byte[] collation, byte[] value)
    {
        var payload = new List<byte> { 0x01, 0x00 };
        payload.AddRange(Encoding.Unicode.GetBytes("p"));
        payload.AddRange([0x00, 0x00]); // option flags
        payload.Add(0x02); // parameter name length, in characters
        payload.AddRange(Encoding.Unicode.GetBytes("@v"));
        payload.Add(0x00); // status
        payload.Add(0xA7); // BIGVARCHRTYPE
        payload.AddRange([0x40, 0x1F]); // max length: 8000
        payload.AddRange(collation);
        payload.AddRange([(byte)value.Length, 0x00]); // value length
        payload.AddRange(value);
        return [.. payload];
    }
}
