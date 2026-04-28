using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using Meziantou.Framework.PostgreSql.Handler;

namespace Meziantou.Framework.PostgreSql.Protocol;

internal static class PostgreSqlResponseSerializer
{
    public static byte[] CreateAuthenticationOk()
    {
        var payload = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(payload, 0);
        return payload;
    }

    public static byte[] CreateAuthenticationClearTextPassword()
    {
        var payload = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(payload, 3);
        return payload;
    }

    public static byte[] CreateAuthenticationMd5Password(byte[] salt)
    {
        ArgumentNullException.ThrowIfNull(salt);
        if (salt.Length != 4)
        {
            throw new ArgumentException("MD5 authentication salt must contain exactly 4 bytes.", nameof(salt));
        }

        var payload = new byte[8];
        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(0, 4), 5);
        salt.CopyTo(payload.AsSpan(4, 4));
        return payload;
    }

    public static byte[] CreateAuthenticationSasl(IReadOnlyList<string> mechanisms)
    {
        ArgumentNullException.ThrowIfNull(mechanisms);

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        WriteInt32BigEndian(writer, 10);
        foreach (var mechanism in mechanisms)
        {
            WriteNullTerminatedString(writer, mechanism);
        }

        writer.Write((byte)0);
        writer.Flush();
        return stream.ToArray();
    }

    public static byte[] CreateAuthenticationSaslContinue(string serverFirstMessage)
    {
        ArgumentException.ThrowIfNullOrEmpty(serverFirstMessage);
        return CreateAuthenticationSaslMessage(11, serverFirstMessage);
    }

    public static byte[] CreateAuthenticationSaslFinal(string serverFinalMessage)
    {
        ArgumentException.ThrowIfNullOrEmpty(serverFinalMessage);
        return CreateAuthenticationSaslMessage(12, serverFinalMessage);
    }

    public static byte[] CreateParameterStatus(string key, string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(value);

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        WriteNullTerminatedString(writer, key);
        WriteNullTerminatedString(writer, value);
        writer.Flush();
        return stream.ToArray();
    }

    public static byte[] CreateBackendKeyData(int processId, int secretKey)
    {
        var payload = new byte[8];
        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(0, 4), processId);
        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(4, 4), secretKey);
        return payload;
    }

    public static byte[] CreateReadyForQuery(byte transactionStatus = (byte)'I')
    {
        return [transactionStatus];
    }

    public static byte[] CreateErrorResponse(string severity, string sqlState, string message)
    {
        return CreateErrorOrNoticeResponse(severity, sqlState, message);
    }

    public static byte[] CreateErrorResponse(PostgreSqlQueryError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return CreateErrorOrNoticeResponse(error.Severity, error.Code, error.Message);
    }

    public static byte[] CreateNoticeResponse(string message)
    {
        return CreateErrorOrNoticeResponse("NOTICE", "00000", message);
    }

    public static byte[] CreateParseComplete()
    {
        return [];
    }

    public static byte[] CreateBindComplete()
    {
        return [];
    }

    public static byte[] CreateCloseComplete()
    {
        return [];
    }

    public static byte[] CreateNoData()
    {
        return [];
    }

    public static byte[] CreateParameterDescription(IReadOnlyList<uint> parameterTypeOids)
    {
        ArgumentNullException.ThrowIfNull(parameterTypeOids);

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        WriteInt16BigEndian(writer, checked((short)parameterTypeOids.Count));
        foreach (var parameterTypeOid in parameterTypeOids)
        {
            WriteUInt32BigEndian(writer, parameterTypeOid);
        }

        writer.Flush();
        return stream.ToArray();
    }

    public static byte[] CreateRowDescription(PostgreSqlResultSet resultSet)
    {
        ArgumentNullException.ThrowIfNull(resultSet);

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        WriteInt16BigEndian(writer, checked((short)resultSet.Columns.Count));
        foreach (var column in resultSet.Columns)
        {
            WriteNullTerminatedString(writer, column.Name);
            WriteInt32BigEndian(writer, 0);
            WriteInt16BigEndian(writer, 0);
            WriteUInt32BigEndian(writer, PostgreSqlTypeMapper.GetTypeOid(column.ColumnType));
            WriteInt16BigEndian(writer, PostgreSqlTypeMapper.GetTypeSize(column.ColumnType));
            WriteInt32BigEndian(writer, -1);
            WriteInt16BigEndian(writer, 0);
        }

        writer.Flush();
        return stream.ToArray();
    }

    public static byte[] CreateDataRow(PostgreSqlResultSet resultSet, IReadOnlyList<object?> row)
    {
        ArgumentNullException.ThrowIfNull(resultSet);
        ArgumentNullException.ThrowIfNull(row);
        if (row.Count != resultSet.Columns.Count)
        {
            throw new InvalidOperationException("Row value count must match column count.");
        }

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        WriteInt16BigEndian(writer, checked((short)row.Count));
        for (var i = 0; i < row.Count; i++)
        {
            var value = row[i];
            if (value is null)
            {
                WriteInt32BigEndian(writer, -1);
                continue;
            }

            var bytes = PostgreSqlValueConverter.EncodeResultValue(resultSet.Columns[i].ColumnType, value);
            WriteInt32BigEndian(writer, bytes.Length);
            writer.Write(bytes);
        }

        writer.Flush();
        return stream.ToArray();
    }

    public static byte[] CreateCommandComplete(string commandTag, int rowCount)
    {
        ArgumentException.ThrowIfNullOrEmpty(commandTag);
        var tag = rowCount >= 0 ? $"{commandTag} {rowCount.ToString(CultureInfo.InvariantCulture)}" : commandTag;

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        WriteNullTerminatedString(writer, tag);
        writer.Flush();
        return stream.ToArray();
    }

    private static byte[] CreateAuthenticationSaslMessage(int code, string payloadText)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        WriteInt32BigEndian(writer, code);
        writer.Write(Encoding.UTF8.GetBytes(payloadText));
        writer.Flush();
        return stream.ToArray();
    }

    private static byte[] CreateErrorOrNoticeResponse(string severity, string sqlState, string message)
    {
        ArgumentException.ThrowIfNullOrEmpty(severity);
        ArgumentException.ThrowIfNullOrEmpty(sqlState);
        ArgumentException.ThrowIfNullOrEmpty(message);

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        WriteField(writer, (byte)'S', severity);
        WriteField(writer, (byte)'V', severity);
        WriteField(writer, (byte)'C', sqlState);
        WriteField(writer, (byte)'M', message);
        writer.Write((byte)0);
        writer.Flush();
        return stream.ToArray();
    }

    private static void WriteField(BinaryWriter writer, byte code, string value)
    {
        writer.Write(code);
        WriteNullTerminatedString(writer, value);
    }

    private static void WriteNullTerminatedString(BinaryWriter writer, string value)
    {
        writer.Write(Encoding.UTF8.GetBytes(value));
        writer.Write((byte)0);
    }

    private static void WriteInt16BigEndian(BinaryWriter writer, short value)
    {
        Span<byte> buffer = stackalloc byte[2];
        BinaryPrimitives.WriteInt16BigEndian(buffer, value);
        writer.Write(buffer);
    }

    private static void WriteInt32BigEndian(BinaryWriter writer, int value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(buffer, value);
        writer.Write(buffer);
    }

    private static void WriteUInt32BigEndian(BinaryWriter writer, uint value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(buffer, value);
        writer.Write(buffer);
    }
}
