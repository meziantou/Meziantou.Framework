using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Fuzz;

// The state machine of a WebUSB device (https://usb.spec.whatwg.org/#usbdevice): sequences of operations on a device whose
// descriptors and answers come from the input
public static partial class WebPlatformFuzzTargets
{
    private const byte UsbOpen = 0;
    private const byte UsbClose = 1;
    private const byte UsbSelectConfiguration = 2;
    private const byte UsbClaimInterface = 3;
    private const byte UsbReleaseInterface = 4;
    private const byte UsbSelectAlternateInterface = 5;
    private const byte UsbControlTransferIn = 6;
    private const byte UsbControlTransferOut = 7;
    private const byte UsbTransferIn = 8;
    private const byte UsbTransferOut = 9;
    private const byte UsbIsochronousTransferIn = 10;
    private const byte UsbIsochronousTransferOut = 11;
    private const byte UsbClearHalt = 12;
    private const byte UsbReset = 13;
    private const byte UsbRunTasks = 14;
    private const byte UsbDisconnect = 15;
    private const byte UsbConstruct = 16;
    private const byte UsbOperationCount = 17;

    private static readonly HashSet<string> UsbErrorNames = new(StringComparer.Ordinal)
    {
        "NotFoundError", "InvalidStateError", "NetworkError", "SecurityError", "TimeoutError", "AbortError", "InvalidAccessError", "IndexSizeError", "DataError",
    };

    /// <summary>
    /// Builds a device from the input (its configurations, interfaces, alternate settings and endpoints, and the answer of
    /// each operation: success, a transfer status, an error or no answer), then runs a sequence of operations on its
    /// <c>USBDevice</c>, and checks the properties of the state: the operations fail only with the errors of WebUSB, every
    /// task completes once the tasks ran unless the device did not answer, the current configuration is one of the
    /// configurations of the device, a claimed interface belongs to the current configuration of an opened device, and the
    /// alternate setting of an interface is one of its alternate settings.
    /// </summary>
    public static void WebUsb(ReadOnlySpan<byte> data)
    {
        var descriptor = CreateFuzzUsbDescriptor(ref data);
        var host = new FuzzUsbDevice(descriptor, data.ToArray());
        var provider = new FuzzUsbProvider();
        using var context = new BrowsingContext(new BrowsingContextOptions { Usb = new UsbOptions { Provider = provider } });
        var document = context.Navigate("https://example.com/", _ => "<!DOCTYPE html>");
        context.EventLoop.RunUntilIdle();
        var usb = document.DefaultView!.Navigator.Usb;
        provider.Connect(host);
        var devices = usb.GetDevices();
        context.EventLoop.RunUntilIdle();
        var device = devices.Result.Single();

        var tasks = new List<Task>();

        // The disconnection is processed by a task of the event loop: the device is closed once the tasks ran
        var disconnecting = false;
        var disconnected = false;
        var operations = Math.Min(data.Length, 200);
        for (var i = 0; i < operations && !data.IsEmpty; i++)
        {
            var operation = (byte)(FuzzInput.ReadByte(ref data) % UsbOperationCount);
            var a = data.IsEmpty ? (byte)0 : FuzzInput.ReadByte(ref data);
            var b = data.IsEmpty ? (byte)0 : FuzzInput.ReadByte(ref data);
            switch (operation)
            {
                case UsbOpen:
                    tasks.Add(device.Open());
                    break;

                case UsbClose:
                    tasks.Add(device.Close());
                    break;

                case UsbSelectConfiguration:
                    tasks.Add(device.SelectConfiguration((byte)(a % 4)));
                    break;

                case UsbClaimInterface:
                    tasks.Add(device.ClaimInterface((byte)(a % 4)));
                    break;

                case UsbReleaseInterface:
                    tasks.Add(device.ReleaseInterface((byte)(a % 4)));
                    break;

                case UsbSelectAlternateInterface:
                    tasks.Add(device.SelectAlternateInterface((byte)(a % 4), (byte)(b % 3)));
                    break;

                case UsbControlTransferIn:
                case UsbControlTransferOut:
                {
                    var setup = new UsbControlTransferParameters
                    {
                        RequestType = (UsbRequestType)(a % 3),
                        Recipient = (UsbRecipient)((a >> 2) % 4),
                        Request = b,
                        Value = a,
                        Index = (ushort)((b & 0x80) | (b % 4)),
                    };
                    tasks.Add(operation == UsbControlTransferIn ? device.ControlTransferIn(setup, b) : device.ControlTransferOut(setup, new byte[b % 16]));
                    break;
                }

                case UsbTransferIn:
                    tasks.Add(device.TransferIn((byte)(a % 18), b));
                    break;

                case UsbTransferOut:
                    tasks.Add(device.TransferOut((byte)(a % 18), new byte[b % 16]));
                    break;

                case UsbIsochronousTransferIn:
                    tasks.Add(device.IsochronousTransferIn((byte)(a % 18), [(uint)(b % 8), (uint)(a % 8)]));
                    break;

                case UsbIsochronousTransferOut:
                    tasks.Add(device.IsochronousTransferOut((byte)(a % 18), new byte[(b % 8) + (a % 8) + (b & 1)], [(uint)(b % 8), (uint)(a % 8)]));
                    break;

                case UsbClearHalt:
                    tasks.Add(device.ClearHalt((UsbDirection)(a & 1), (byte)(b % 18)));
                    break;

                case UsbReset:
                    tasks.Add(device.Reset());
                    break;

                case UsbRunTasks:
                    context.EventLoop.RunUntilIdle();
                    disconnected = disconnecting;
                    break;

                case UsbDisconnect:
                    disconnecting |= provider.Disconnect(host);
                    break;

                case UsbConstruct:
                    ConstructUsbObjects(device, a, b);
                    break;
            }

            CheckUsbDeviceState(device, disconnected);
        }

        context.EventLoop.RunUntilIdle();
        CheckUsbDeviceState(device, disconnecting);
        foreach (var task in tasks)
        {
            FuzzAssert.True(task.IsCompleted || host.HasStalledOperations, "An operation did not complete although the device answered all the operations");
            if (task.IsFaulted)
            {
                var exception = task.Exception.InnerException;
                FuzzAssert.True(exception is DomException domException && UsbErrorNames.Contains(domException.Name), $"An operation failed with an unexpected error: {exception}");
            }
        }
    }

    private static void ConstructUsbObjects(UsbDevice device, byte a, byte b)
    {
        try
        {
            var configuration = new UsbConfiguration(device, (byte)(a % 4));
            var usbInterface = new UsbInterface(configuration, (byte)(b % 4));
            var alternate = new UsbAlternateInterface(usbInterface, (byte)(a % 3));
            var endpoint = new UsbEndpoint(alternate, (byte)(b % 18), (UsbDirection)(a & 1));
            FuzzAssert.Equal(alternate.AlternateSetting, endpoint.Alternate.AlternateSetting, "The endpoint is not in its alternate setting");
        }
        catch (RangeErrorException)
        {
        }
    }

    private static void CheckUsbDeviceState(UsbDevice device, bool disconnected)
    {
        if (disconnected)
        {
            FuzzAssert.False(device.Opened, "A disconnected device is opened");
        }

        if (device.Configuration is not { } configuration)
            return;

        FuzzAssert.True(device.Configurations.Contains(configuration), "The current configuration is not one of the configurations of the device");
        foreach (var usbInterface in configuration.Interfaces)
        {
            FuzzAssert.True(usbInterface.Alternates.Contains(usbInterface.Alternate), "The alternate setting of an interface is not one of its alternate settings");
            if (usbInterface.Claimed)
            {
                FuzzAssert.True(device.Opened, "A claimed interface belongs to a closed device");
            }
        }

        foreach (var other in device.Configurations)
        {
            if (!ReferenceEquals(other, configuration))
            {
                FuzzAssert.False(other.Interfaces.Any(usbInterface => usbInterface.Claimed), "An interface of another configuration is claimed");
            }
        }
    }

    // The descriptors: up to 3 configurations (values 1 to 3) of up to 3 interfaces, of up to 2 alternate settings, of up to
    // 3 endpoints (the classes include the protected classes, the endpoint numbers include the invalid ones)
    private static UsbDeviceDescriptor CreateFuzzUsbDescriptor(ref ReadOnlySpan<byte> data)
    {
        byte Next(ref ReadOnlySpan<byte> input) => input.IsEmpty ? (byte)0 : FuzzInput.ReadByte(ref input);

        var configurations = new List<UsbConfigurationDescriptor>();
        var configurationCount = Next(ref data) % 4;
        for (var c = 0; c < configurationCount; c++)
        {
            var interfaces = new List<UsbInterfaceDescriptor>();
            var interfaceCount = Next(ref data) % 4;
            for (var i = 0; i < interfaceCount; i++)
            {
                var alternates = new List<UsbAlternateInterfaceDescriptor>();
                var alternateCount = 1 + (Next(ref data) % 2);
                for (var alt = 0; alt < alternateCount; alt++)
                {
                    var endpoints = new List<UsbEndpointDescriptor>();
                    var endpointCount = Next(ref data) % 4;
                    for (var e = 0; e < endpointCount; e++)
                    {
                        var value = Next(ref data);
                        endpoints.Add(new UsbEndpointDescriptor { EndpointNumber = (byte)(value % 17), Direction = (UsbDirection)(value >> 7), Type = (UsbEndpointType)((value >> 5) % 3), PacketSize = 64 });
                    }

                    alternates.Add(new UsbAlternateInterfaceDescriptor { AlternateSetting = (byte)alt, InterfaceClass = (Next(ref data) % 4) switch { 0 => 0x03, 1 => 0x08, _ => 0xFF }, Endpoints = endpoints });
                }

                interfaces.Add(new UsbInterfaceDescriptor { InterfaceNumber = (byte)(i * (1 + (Next(ref data) % 2))), Alternates = alternates });
            }

            configurations.Add(new UsbConfigurationDescriptor { ConfigurationValue = (byte)(c + 1), Interfaces = interfaces });
        }

        return new UsbDeviceDescriptor { VendorId = 0x1234, ProductId = 0x5678, ActiveConfigurationValue = (byte)(Next(ref data) % 4), Configurations = configurations };
    }

    private sealed class FuzzUsbProvider : UsbProvider
    {
        public override bool IsAllowed(UsbHostDevice device, string origin) => true;
    }

    // A device whose answers are the bytes of the input: success, a stall or a babble, an error, or no answer
    private sealed class FuzzUsbDevice(UsbDeviceDescriptor descriptor, byte[] answers) : UsbHostDevice(descriptor)
    {
        private int _position;

        public bool HasStalledOperations { get; private set; }

        public override Task<UsbDeviceSession> OpenAsync(CancellationToken cancellationToken) => Answer<UsbDeviceSession>(_ => new FuzzUsbSession(this), cancellationToken);

        public Task<T> Answer<T>(Func<UsbTransferStatus, T> result, CancellationToken cancellationToken)
        {
            var answer = _position < answers.Length ? answers[_position++] : (byte)0;
            switch (answer % 16)
            {
                case 13:
                    HasStalledOperations = true;
                    var stalled = new TaskCompletionSource<T>();
                    cancellationToken.Register(() => stalled.TrySetCanceled(cancellationToken));
                    return stalled.Task;

                case 14:
                    return Task.FromException<T>(new UsbException((UsbError)(answer / 16 % 5)));

                case 15:
                    return Task.FromException<T>(new InvalidOperationException("The device failed"));

                default:
                    return Task.FromResult(result((UsbTransferStatus)(answer % 3)));
            }
        }
    }

    private sealed class FuzzUsbSession(FuzzUsbDevice device) : UsbDeviceSession
    {
        public override Task CloseAsync() => Task.CompletedTask;

        public override Task SelectConfigurationAsync(byte configurationValue, CancellationToken cancellationToken) => device.Answer(_ => true, cancellationToken);

        public override Task ClaimInterfaceAsync(byte interfaceNumber, CancellationToken cancellationToken) => device.Answer(_ => true, cancellationToken);

        public override Task ReleaseInterfaceAsync(byte interfaceNumber, CancellationToken cancellationToken) => device.Answer(_ => true, cancellationToken);

        public override Task SelectAlternateInterfaceAsync(byte interfaceNumber, byte alternateSetting, CancellationToken cancellationToken) => device.Answer(_ => true, cancellationToken);

        public override Task ResetAsync(CancellationToken cancellationToken) => device.Answer(_ => true, cancellationToken);

        public override Task ClearHaltAsync(UsbDirection direction, byte endpointNumber, CancellationToken cancellationToken) => device.Answer(_ => true, cancellationToken);

        public override Task<UsbInTransfer> ControlTransferInAsync(UsbControlTransferSetup setup, ushort length, CancellationToken cancellationToken)
            => device.Answer(status => new UsbInTransfer(status, new byte[length % 9]), cancellationToken);

        public override Task<UsbOutTransfer> ControlTransferOutAsync(UsbControlTransferSetup setup, ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
            => device.Answer(status => new UsbOutTransfer(status, (uint)data.Length), cancellationToken);

        public override Task<UsbInTransfer> TransferInAsync(byte endpointNumber, uint length, CancellationToken cancellationToken)
            => device.Answer(status => new UsbInTransfer(status, new byte[length % 9]), cancellationToken);

        public override Task<UsbOutTransfer> TransferOutAsync(byte endpointNumber, ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
            => device.Answer(status => new UsbOutTransfer(status, (uint)data.Length), cancellationToken);

        // The device may return more data than requested, or fewer packets than requested
        public override Task<IReadOnlyList<UsbInTransfer>> IsochronousTransferInAsync(byte endpointNumber, IReadOnlyList<uint> packetLengths, CancellationToken cancellationToken)
            => device.Answer<IReadOnlyList<UsbInTransfer>>(status => [.. packetLengths.Skip(status == UsbTransferStatus.Babble ? 1 : 0).Select(length => new UsbInTransfer(status, new byte[length + 1]))], cancellationToken);

        public override Task<IReadOnlyList<UsbOutTransfer>> IsochronousTransferOutAsync(byte endpointNumber, ReadOnlyMemory<byte> data, IReadOnlyList<uint> packetLengths, CancellationToken cancellationToken)
            => device.Answer<IReadOnlyList<UsbOutTransfer>>(status => [.. packetLengths.Select(length => new UsbOutTransfer(status, length))], cancellationToken);
    }

    private static IEnumerable<FuzzSeed> GetWebUsbSeeds()
    {
        byte[][] scenarios =
        [
            // One configuration with an interface of 2 alternate settings: open, configure, claim, select, transfer, close
            [1, 1, 1, 1, 0x81, 3, 1, 2, 0xA1, 0x01, 3, 0, UsbOpen, 0, 0, UsbRunTasks, 0, 0, UsbSelectConfiguration, 1, 0, UsbRunTasks, 0, 0, UsbClaimInterface, 0, 0, UsbRunTasks, 0, 0, UsbSelectAlternateInterface, 0, 1, UsbRunTasks, 0, 0, UsbTransferIn, 1, 8, UsbIsochronousTransferIn, 1, 4, UsbRunTasks, 0, 0, UsbClose, 0, 0],

            // Concurrent operations while the state of the device changes
            [2, 2, 0, 1, 0x02, 3, 0, 0, 0, 0, UsbOpen, 0, 0, UsbOpen, 0, 0, UsbClose, 0, 0, UsbRunTasks, 0, 0, UsbSelectConfiguration, 2, 0, UsbClaimInterface, 0, 0, UsbReset, 0, 0, UsbRunTasks, 0, 0, UsbReleaseInterface, 0, 0, UsbClaimInterface, 1, 0, UsbRunTasks, 0, 0],

            // A device that does not answer, then is disconnected
            [1, 1, 0, 1, 0x05, 3, 0, 1, UsbOpen, 0, 0, UsbRunTasks, 0, 0, UsbSelectConfiguration, 1, 0, UsbRunTasks, 0, 0, UsbClaimInterface, 0, 0, UsbRunTasks, 0, 0, UsbTransferOut, 5, 4, UsbDisconnect, 0, 0, UsbRunTasks, 0, 0, UsbOpen, 0, 0, UsbConstruct, 1, 0],

            // Control transfers to the recipients, and protected interfaces
            [1, 2, 0, 0, 0, 0, 1, 0, 0, 1, UsbOpen, 0, 0, UsbRunTasks, 0, 0, UsbControlTransferIn, 0x05, 1, UsbControlTransferOut, 0x09, 0x81, UsbSelectConfiguration, 1, 0, UsbRunTasks, 0, 0, UsbClaimInterface, 0, 0, UsbClaimInterface, 1, 0, UsbRunTasks, 0, 0, UsbControlTransferIn, 0x04, 0, UsbClearHalt, 1, 16],
        ];

        for (var i = 0; i < scenarios.Length; i++)
        {
            yield return new FuzzSeed("webusb-" + i.ToString(System.Globalization.CultureInfo.InvariantCulture), scenarios[i]);
        }
    }
}
