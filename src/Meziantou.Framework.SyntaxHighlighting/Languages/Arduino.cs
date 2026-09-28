using Meziantou.Framework.SyntaxHighlighting.Engine;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

/// <remarks>
/// The Arduino language is C++ with a few more types, constants and library classes, added to every mode of the C++
/// grammar that knows its keywords. As in highlight.js, the Arduino functions (<c>setup</c>, <c>digitalWrite</c>,
/// <c>delay</c>, …) are not keywords (highlight.js only lists them to detect the language): their calls are highlighted
/// like any other C++ function call.
/// </remarks>
internal static class Arduino
{
    private static readonly string[] Types = ["boolean", "byte", "word", "String"];

    private static readonly string[] Literals =
    [
        "DIGITAL_MESSAGE", "FIRMATA_STRING", "ANALOG_MESSAGE", "REPORT_DIGITAL", "REPORT_ANALOG", "INPUT_PULLUP",
        "SET_PIN_MODE", "INTERNAL2V56", "SYSTEM_RESET", "LED_BUILTIN", "INTERNAL1V1", "SYSEX_START", "INTERNAL", "EXTERNAL",
        "DEFAULT", "OUTPUT", "INPUT", "HIGH", "LOW",
    ];

    // Deviation from highlight.js: `IPAddress` is highlighted like the other classes. Upstream, it was also listed with
    // the functions, which are not highlighted, and that list won.
    private static readonly string[] BuiltIn =
    [
        "KeyboardController", "MouseController", "SoftwareSerial", "EthernetServer", "EthernetClient", "LiquidCrystal",
        "RobotControl", "GSMVoiceCall", "EthernetUDP", "EsploraTFT", "HttpClient", "RobotMotor", "WiFiClient", "GSMScanner",
        "FileSystem", "Scheduler", "GSMServer", "YunClient", "YunServer", "IPAddress", "GSMClient", "GSMModem", "Keyboard",
        "Ethernet", "Console", "GSMBand", "Esplora", "Stepper", "Process", "WiFiUDP", "GSM_SMS", "Mailbox", "USBHost",
        "Firmata", "PImage", "Client", "Server", "GSMPIN", "FileIO", "Bridge", "Serial", "EEPROM", "Stream", "Mouse",
        "Audio", "Servo", "File", "Task", "GPRS", "WiFi", "Wire", "TFT", "GSM", "SPI", "SD",
    ];

    public static CompiledMode Instance { get; } = Compiler.Compile(Cpp.CreateMode([.. Cpp.ReservedTypes, .. Types], [.. Cpp.Literals, .. Literals], [.. Cpp.BuiltIn, .. BuiltIn]));
}
