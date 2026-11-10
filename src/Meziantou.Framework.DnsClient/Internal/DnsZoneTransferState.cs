using Meziantou.Framework.DnsClient.Response;
using Meziantou.Framework.DnsClient.Response.Records;

namespace Meziantou.Framework.DnsClient.Internal;

/// <summary>
/// Follows the records of a zone transfer to find its last message: an AXFR (RFC 5936) ends with the SOA record it
/// starts with, an IXFR (RFC 1995) with the SOA record of the new version after its last sequence of differences.
/// </summary>
internal sealed class DnsZoneTransferState
{
    private readonly uint? _clientSerial;
    private Mode _mode;
    private uint _serial;

    public DnsZoneTransferState(uint? clientSerial)
    {
        _clientSerial = clientSerial;
    }

    private enum Mode
    {
        Start,
        AfterFirstSoa,
        Full,
        Deletions,
        Additions,
        Done,
    }

    public bool IsComplete => _mode is Mode.Done;

    /// <summary>Processes the answers of a message, and returns whether the transfer is complete.</summary>
    /// <exception cref="DnsProtocolException">The records do not form a valid transfer.</exception>
    public bool Process(IReadOnlyList<DnsRecord> answers)
    {
        foreach (var record in answers)
        {
            var soa = record as DnsSoaRecord;
            switch (_mode)
            {
                case Mode.Start:
                    _serial = soa?.Serial ?? throw new DnsProtocolException($"A zone transfer must start with an SOA record, not {record.RecordType}.");

                    // RFC 1995 section 2: the server answers a client that is up to date with its SOA record alone
                    _mode = _clientSerial is { } clientSerial && !IsNewer(_serial, clientSerial) ? Mode.Done : Mode.AfterFirstSoa;
                    break;

                case Mode.AfterFirstSoa when soa is null:
                    _mode = Mode.Full;
                    break;

                // An IXFR answered with differences: the SOA record of the version of the client starts the deletions
                case Mode.AfterFirstSoa when soa.Serial == _clientSerial:
                    _mode = Mode.Deletions;
                    break;

                // A full transfer of a zone that holds only its SOA record
                case Mode.AfterFirstSoa:
                    EndFullTransfer(soa);
                    break;

                case Mode.Full when soa is not null:
                    EndFullTransfer(soa);
                    break;

                case Mode.Deletions when soa is not null:
                    _mode = Mode.Additions;
                    break;

                case Mode.Additions when soa is not null:
                    _mode = soa.Serial == _serial ? Mode.Done : Mode.Deletions;
                    break;

                case Mode.Done:
                    throw new DnsProtocolException("The zone transfer has records after its final SOA record.");
            }
        }

        return IsComplete;
    }

    private void EndFullTransfer(DnsSoaRecord soa)
    {
        if (soa.Serial != _serial)
            throw new DnsProtocolException($"The zone transfer ends with the serial {soa.Serial}, but started with {_serial}.");

        _mode = Mode.Done;
    }

    /// <summary>Serial number arithmetic (RFC 1982): whether <paramref name="serial"/> is newer than <paramref name="other"/>.</summary>
    private static bool IsNewer(uint serial, uint other) => serial != other && (int)(serial - other) > 0;
}
