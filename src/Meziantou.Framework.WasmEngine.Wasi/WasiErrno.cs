namespace Meziantou.Framework.WasmEngine.Wasi;

/// <summary>The error codes of WASI Preview 1 (<c>errno</c>), returned by its functions.</summary>
#pragma warning disable CA1028 // The values are the 16-bit codes of WASI
public enum WasiErrno : ushort
#pragma warning restore CA1028
{
    /// <summary>No error occurred.</summary>
    Success = 0,

    /// <summary>Argument list too long (<c>2big</c>).</summary>
    TooBig = 1,

    /// <summary>Permission denied (<c>acces</c>).</summary>
    AccessDenied = 2,

    /// <summary>Address in use (<c>addrinuse</c>).</summary>
    AddressInUse = 3,

    /// <summary>Address not available (<c>addrnotavail</c>).</summary>
    AddressNotAvailable = 4,

    /// <summary>Address family not supported (<c>afnosupport</c>).</summary>
    AddressFamilyNotSupported = 5,

    /// <summary>Resource unavailable, or operation would block (<c>again</c>).</summary>
    Again = 6,

    /// <summary>Connection already in progress (<c>already</c>).</summary>
    Already = 7,

    /// <summary>Bad file descriptor (<c>badf</c>).</summary>
    BadFileDescriptor = 8,

    /// <summary>Bad message (<c>badmsg</c>).</summary>
    BadMessage = 9,

    /// <summary>Device or resource busy (<c>busy</c>).</summary>
    Busy = 10,

    /// <summary>Operation canceled (<c>canceled</c>).</summary>
    Canceled = 11,

    /// <summary>No child processes (<c>child</c>).</summary>
    NoChild = 12,

    /// <summary>Connection aborted (<c>connaborted</c>).</summary>
    ConnectionAborted = 13,

    /// <summary>Connection refused (<c>connrefused</c>).</summary>
    ConnectionRefused = 14,

    /// <summary>Connection reset (<c>connreset</c>).</summary>
    ConnectionReset = 15,

    /// <summary>Resource deadlock would occur (<c>deadlk</c>).</summary>
    Deadlock = 16,

    /// <summary>Destination address required (<c>destaddrreq</c>).</summary>
    DestinationAddressRequired = 17,

    /// <summary>Mathematics argument out of domain of function (<c>dom</c>).</summary>
    Domain = 18,

    /// <summary>Reserved (<c>dquot</c>).</summary>
    DiskQuota = 19,

    /// <summary>File exists (<c>exist</c>).</summary>
    Exists = 20,

    /// <summary>Bad address (<c>fault</c>): a pointer or a length is outside of the memory.</summary>
    Fault = 21,

    /// <summary>File too large (<c>fbig</c>).</summary>
    FileTooLarge = 22,

    /// <summary>Host is unreachable (<c>hostunreach</c>).</summary>
    HostUnreachable = 23,

    /// <summary>Identifier removed (<c>idrm</c>).</summary>
    IdentifierRemoved = 24,

    /// <summary>Illegal byte sequence (<c>ilseq</c>): a path is not valid UTF-8.</summary>
    IllegalByteSequence = 25,

    /// <summary>Operation in progress (<c>inprogress</c>).</summary>
    InProgress = 26,

    /// <summary>Interrupted function (<c>intr</c>).</summary>
    Interrupted = 27,

    /// <summary>Invalid argument (<c>inval</c>).</summary>
    InvalidArgument = 28,

    /// <summary>I/O error (<c>io</c>).</summary>
    IOError = 29,

    /// <summary>Socket is connected (<c>isconn</c>).</summary>
    IsConnected = 30,

    /// <summary>Is a directory (<c>isdir</c>).</summary>
    IsDirectory = 31,

    /// <summary>Too many levels of symbolic links (<c>loop</c>).</summary>
    Loop = 32,

    /// <summary>File descriptor value too large (<c>mfile</c>).</summary>
    TooManyOpenFiles = 33,

    /// <summary>Too many links (<c>mlink</c>).</summary>
    TooManyLinks = 34,

    /// <summary>Message too large (<c>msgsize</c>).</summary>
    MessageTooLarge = 35,

    /// <summary>Reserved (<c>multihop</c>).</summary>
    Multihop = 36,

    /// <summary>Filename too long (<c>nametoolong</c>).</summary>
    NameTooLong = 37,

    /// <summary>Network is down (<c>netdown</c>).</summary>
    NetworkDown = 38,

    /// <summary>Connection aborted by network (<c>netreset</c>).</summary>
    NetworkReset = 39,

    /// <summary>Network unreachable (<c>netunreach</c>).</summary>
    NetworkUnreachable = 40,

    /// <summary>Too many files open in system (<c>nfile</c>).</summary>
    TooManyOpenFilesInSystem = 41,

    /// <summary>No buffer space available (<c>nobufs</c>).</summary>
    NoBufferSpace = 42,

    /// <summary>No such device (<c>nodev</c>).</summary>
    NoDevice = 43,

    /// <summary>No such file or directory (<c>noent</c>).</summary>
    NoEntry = 44,

    /// <summary>Executable file format error (<c>noexec</c>).</summary>
    ExecutableFormatError = 45,

    /// <summary>No locks available (<c>nolck</c>).</summary>
    NoLock = 46,

    /// <summary>Reserved (<c>nolink</c>).</summary>
    NoLink = 47,

    /// <summary>Not enough space (<c>nomem</c>).</summary>
    NoMemory = 48,

    /// <summary>No message of the desired type (<c>nomsg</c>).</summary>
    NoMessage = 49,

    /// <summary>Protocol not available (<c>noprotoopt</c>).</summary>
    NoProtocolOption = 50,

    /// <summary>No space left on device (<c>nospc</c>).</summary>
    NoSpace = 51,

    /// <summary>Function not supported (<c>nosys</c>).</summary>
    NotImplemented = 52,

    /// <summary>The socket is not connected (<c>notconn</c>).</summary>
    NotConnected = 53,

    /// <summary>Not a directory or a symbolic link to a directory (<c>notdir</c>).</summary>
    NotDirectory = 54,

    /// <summary>Directory not empty (<c>notempty</c>).</summary>
    NotEmpty = 55,

    /// <summary>State not recoverable (<c>notrecoverable</c>).</summary>
    NotRecoverable = 56,

    /// <summary>Not a socket (<c>notsock</c>).</summary>
    NotSocket = 57,

    /// <summary>Not supported, or operation not supported on socket (<c>notsup</c>).</summary>
    NotSupported = 58,

    /// <summary>Inappropriate I/O control operation (<c>notty</c>).</summary>
    NotTerminal = 59,

    /// <summary>No such device or address (<c>nxio</c>).</summary>
    NoDeviceOrAddress = 60,

    /// <summary>Value too large to be stored in data type (<c>overflow</c>).</summary>
    Overflow = 61,

    /// <summary>Previous owner died (<c>ownerdead</c>).</summary>
    OwnerDead = 62,

    /// <summary>Operation not permitted (<c>perm</c>).</summary>
    NotPermitted = 63,

    /// <summary>Broken pipe (<c>pipe</c>).</summary>
    BrokenPipe = 64,

    /// <summary>Protocol error (<c>proto</c>).</summary>
    ProtocolError = 65,

    /// <summary>Protocol not supported (<c>protonosupport</c>).</summary>
    ProtocolNotSupported = 66,

    /// <summary>Protocol wrong type for socket (<c>prototype</c>).</summary>
    ProtocolWrongType = 67,

    /// <summary>Result too large (<c>range</c>).</summary>
    Range = 68,

    /// <summary>Read-only file system (<c>rofs</c>).</summary>
    ReadOnlyFileSystem = 69,

    /// <summary>Invalid seek (<c>spipe</c>): the file descriptor is not seekable.</summary>
    InvalidSeek = 70,

    /// <summary>No such process (<c>srch</c>).</summary>
    NoProcess = 71,

    /// <summary>Reserved (<c>stale</c>).</summary>
    Stale = 72,

    /// <summary>Connection timed out (<c>timedout</c>).</summary>
    TimedOut = 73,

    /// <summary>Text file busy (<c>txtbsy</c>).</summary>
    TextFileBusy = 74,

    /// <summary>Cross-device link (<c>xdev</c>).</summary>
    CrossDevice = 75,

    /// <summary>Extension: capabilities insufficient (<c>notcapable</c>), such as a path that leaves its directory.</summary>
    NotCapable = 76,
}
