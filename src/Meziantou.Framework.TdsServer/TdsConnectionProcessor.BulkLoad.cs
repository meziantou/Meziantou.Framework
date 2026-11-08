using System.Buffers.Binary;
using System.Net;
using System.Security.Claims;
using Meziantou.Framework.Tds.Handler;
using Meziantou.Framework.Tds.Protocol;
using Microsoft.Extensions.Logging;

namespace Meziantou.Framework.Tds;

internal sealed partial class TdsConnectionProcessor
{
    private const ushort DoneCount = 0x0010;
    private const byte DoneMore = 0x01;

    /// <summary>The scale of the decimal columns of a destination table: decimal(38, 10) holds 28 digits before the decimal point.</summary>
    private const byte BulkLoadDecimalScale = 10;

    // sp_tablecollations_100 reports the collation of the text columns. It matches the collation of the result sets.
    private const string DefaultCollationName = "SQL_Latin1_General_CP1_CI_AS";
    private static readonly byte[] DefaultCollationBytes = [0x09, 0x04, 0xD0, 0x00, 0x34];

    /// <summary>
    /// Answers the statements <c>SqlBulkCopy</c> sends before the rows: the query that reads the columns of the
    /// destination table, and <c>insert bulk</c>. Returns <see langword="null"/> for any other request, and when no
    /// bulk load handler is configured, so the query handler answers them.
    /// </summary>
    private async Task<byte[]?> TryExecuteBulkCopyCommandAsync(TdsQueryContext queryContext, TdsSessionState session, int payloadSizePerPacket, CancellationToken cancellationToken)
    {
        if (_options.BulkLoadHandler is not { } handler || queryContext.RequestType != TdsQueryRequestType.SqlBatch || queryContext.CommandText is not { } commandText)
        {
            return null;
        }

        if (TdsBulkCopyCommandParser.TryParseInsertBulk(commandText, out var insertTableName, out var options))
        {
            session.PendingBulkLoad = new TdsPendingBulkLoad(insertTableName, options);
            return TdsResponseSerializer.CreateQueryResponse(new TdsQueryResult(), payloadSizePerPacket);
        }

        if (!TdsBulkCopyCommandParser.TryParseMetadataQuery(commandText, out var tableName, out var readsColumnAliases))
        {
            return null;
        }

        var (name, schema, database) = SplitTableName(tableName);
        IReadOnlyList<TdsColumn>? columns;
        try
        {
            columns = await handler.GetTableColumns(new TdsBulkLoadTableContext
            {
                RemoteEndPoint = queryContext.RemoteEndPoint,
                UserContext = queryContext.UserContext,
                TableName = name,
                SchemaName = schema,
                DatabaseName = database,
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception in bulk load table handler");
            return CreateErrorResponse(50002, "Unhandled bulk load handler exception", payloadSizePerPacket);
        }

        if (columns is null || columns.Count == 0)
        {
            // The message SQL Server returns for a table that does not exist
            return CreateErrorResponse(208, $"Invalid object name '{string.Join('.', tableName)}'.", payloadSizePerPacket);
        }

        return CreateTableMetadataResponse(columns, readsColumnAliases, payloadSizePerPacket);
    }

    private async Task<byte[]> ExecuteBulkLoadAsync(TdsPacket packet, EndPoint remoteEndPoint, ClaimsPrincipal? userContext, TdsSessionState session, int payloadSizePerPacket, CancellationToken cancellationToken)
    {
        var pendingBulkLoad = session.PendingBulkLoad;
        session.PendingBulkLoad = null;
        if (_options.BulkLoadHandler is not { } handler)
        {
            return CreateErrorResponse(50001, "Bulk loads (SqlBulkCopy) are not supported: no bulk load handler is configured.", payloadSizePerPacket);
        }

        if (pendingBulkLoad is null)
        {
            return CreateErrorResponse(4804, "The bulk load data was not preceded by an 'insert bulk' statement.", payloadSizePerPacket);
        }

        IReadOnlyList<TdsColumn> columns;
        IReadOnlyList<IReadOnlyList<object?>> rows;
        try
        {
            (columns, rows) = TdsBulkLoadParser.Parse(packet.Payload);
        }
        catch (InvalidDataException ex)
        {
            _logger.LogDebug(ex, "Failed to parse the bulk load data");
            return CreateErrorResponse(4804, "Invalid bulk load data: " + ex.Message, payloadSizePerPacket);
        }

        var (name, schema, database) = SplitTableName(pendingBulkLoad.TableName);
        TdsBulkLoadResult result;
        try
        {
            result = await handler.LoadRows(new TdsBulkLoadContext
            {
                RemoteEndPoint = remoteEndPoint,
                UserContext = userContext,
                TableName = name,
                SchemaName = schema,
                DatabaseName = database,
                Columns = columns,
                Rows = rows,
                Options = pendingBulkLoad.Options,
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception in bulk load handler");
            return CreateErrorResponse(50002, "Unhandled bulk load handler exception", payloadSizePerPacket);
        }

        if (result.Error is not null)
        {
            return TdsResponseSerializer.CreateQueryResponse(TdsQueryResult.FromError(result.Error), payloadSizePerPacket);
        }

        // DONE with DONE_COUNT: the number of rows copied
        var done = new byte[13];
        done[0] = 0xFD;
        BinaryPrimitives.WriteUInt16LittleEndian(done.AsSpan(1), DoneCount);
        BinaryPrimitives.WriteUInt64LittleEndian(done.AsSpan(5), (ulong)rows.Count);
        return done;
    }

    /// <summary>
    /// Creates the result sets <c>SqlBulkCopy</c> reads to learn the destination table: <c>@@TRANCOUNT</c>, the columns
    /// (an empty result set, as <c>SET FMTONLY ON</c> returns), the output of <c>sp_tablecollations_100</c>, and the
    /// column aliases of graph tables for SqlClient 7 and later.
    /// </summary>
    private static byte[] CreateTableMetadataResponse(IReadOnlyList<TdsColumn> columns, bool readsColumnAliases, int payloadSizePerPacket)
    {
        var transactionCount = new TdsResultSet();
        transactionCount.Columns.Add(new TdsColumn("TranCount", TdsColumnType.Int32));
        transactionCount.Rows.Add([0]);

        var tableColumns = new TdsResultSet();
        foreach (var column in columns)
        {
            tableColumns.Columns.Add(column);
        }

        var collations = new TdsResultSet();
        collations.Columns.Add(new TdsColumn("colid", TdsColumnType.Int32));
        collations.Columns.Add(new TdsColumn("name", TdsColumnType.NVarChar));
        collations.Columns.Add(new TdsColumn("tds_collation_100", TdsColumnType.Binary));
        collations.Columns.Add(new TdsColumn("collation_name", TdsColumnType.NVarChar));
        for (var i = 0; i < columns.Count; i++)
        {
            var isText = columns[i].ColumnType is TdsColumnType.NVarChar or TdsColumnType.Variant or TdsColumnType.UserDefined or TdsColumnType.Table;
            collations.Rows.Add([i + 1, columns[i].Name, isText ? DefaultCollationBytes : null, isText ? DefaultCollationName : null]);
        }

        var otherResults = new TdsQueryResult();
        otherResults.ResultSets.Add(collations);
        if (readsColumnAliases)
        {
            var aliases = new TdsResultSet();
            aliases.Columns.Add(new TdsColumn("Canonical_Column_Name", TdsColumnType.NVarChar));
            aliases.Columns.Add(new TdsColumn("Aliased_Column_Name", TdsColumnType.NVarChar));
            otherResults.ResultSets.Add(aliases);
        }

        var transactionCountResponse = CreateSingleResultResponse(transactionCount, payloadSizePerPacket);
        var tableColumnsResponse = CreateSingleResultResponse(tableColumns, payloadSizePerPacket);
        WidenColumnMetadata(tableColumnsResponse);
        return [.. transactionCountResponse, .. tableColumnsResponse, .. TdsResponseSerializer.CreateQueryResponse(otherResults, payloadSizePerPacket)];

        static byte[] CreateSingleResultResponse(TdsResultSet resultSet, int payloadSizePerPacket)
        {
            var result = new TdsQueryResult();
            result.ResultSets.Add(resultSet);
            var response = TdsResponseSerializer.CreateQueryResponse(result, payloadSizePerPacket);

            // Other result sets follow: set DONE_MORE on the DONE token that ends the response
            response[^12] |= DoneMore;
            return response;
        }
    }

    /// <summary>
    /// Declares the columns of an empty result set with the widest types: the serializer sizes text, binary and decimal
    /// columns from their values, and <c>SqlBulkCopy</c> converts the rows to the declared types, so an empty result set
    /// would limit text to 4000 characters and round decimals to integers.
    /// </summary>
    private static void WidenColumnMetadata(byte[] response)
    {
        if (response.Length < 3 || response[0] != 0x81)
        {
            throw new InvalidOperationException("The response does not start with a COLMETADATA token.");
        }

        var count = BinaryPrimitives.ReadUInt16LittleEndian(response.AsSpan(1));
        var position = 3;
        for (var i = 0; i < count; i++)
        {
            position += 6; // user type and flags
            var typeToken = response[position++];
            switch (typeToken)
            {
                case 0x26 or 0x68 or 0x6D or 0x6E or 0x6F or 0x24 or 0x29 or 0x2A or 0x2B or 0xF1:
                    position++; // length, scale or XML schema flag
                    break;

                case 0x6A or 0x6C:
                    response[position + 2] = BulkLoadDecimalScale;
                    position += 3;
                    break;

                case 0xA5 or 0xAD:
                    BinaryPrimitives.WriteUInt16LittleEndian(response.AsSpan(position), ushort.MaxValue);
                    position += 2;
                    break;

                case 0xE7 or 0xEF or 0xA7 or 0xAF:
                    BinaryPrimitives.WriteUInt16LittleEndian(response.AsSpan(position), ushort.MaxValue);
                    position += 2 + TdsCollation.Size;
                    break;
            }

            position += 1 + (response[position] * 2); // column name
        }
    }

    private static (string Name, string? Schema, string? Database) SplitTableName(IReadOnlyList<string> parts)
    {
        static string? GetPart(IReadOnlyList<string> parts, int indexFromEnd) => parts.Count >= indexFromEnd && parts[^indexFromEnd].Length > 0 ? parts[^indexFromEnd] : null;

        return (parts[^1], GetPart(parts, 2), GetPart(parts, 3));
    }

    private static byte[] CreateErrorResponse(uint number, string message, int payloadSizePerPacket)
    {
        return TdsResponseSerializer.CreateQueryResponse(
            TdsQueryResult.FromError(new TdsQueryError
            {
                Number = number,
                State = 1,
                Class = 16,
                Message = message,
            }),
            payloadSizePerPacket);
    }

    /// <summary>The state of one session, the connection or a MARS session, between its requests.</summary>
    private sealed class TdsSessionState
    {
        /// <summary>Gets or sets the <c>insert bulk</c> statement whose rows the client sends next.</summary>
        public TdsPendingBulkLoad? PendingBulkLoad { get; set; }
    }

    private sealed record TdsPendingBulkLoad(IReadOnlyList<string> TableName, IReadOnlyList<string> Options);
}
