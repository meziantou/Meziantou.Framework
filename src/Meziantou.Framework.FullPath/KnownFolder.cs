namespace Meziantou.Framework;

public sealed class KnownFolder
{
    private KnownFolder(string name, string folderType, Guid knownFolderId, string defaultPath)
    {
        Name = name;
        FolderType = folderType;
        FolderId = knownFolderId;
        DefaultPath = defaultPath;
    }
    public string Name { get; }
    public string FolderType { get; }
    public Guid FolderId { get; }
    public string DefaultPath { get; }

    public override string ToString() => $"{Name} ({FolderId})";

    /// <summary>
    /// Represents the Account Pictures folder.
    /// Default path: <c>%APPDATA%\Microsoft\Windows\AccountPictures</c>.
    /// </summary>
    public static KnownFolder AccountPictures { get; } = new("Account Pictures", "PERUSER", new Guid(0x008ca0b1, 0x55b4, 0x4c56, 0xb8, 0xa8, 0x4d, 0xe4, 0xb2, 0x99, 0xd3, 0xbe), @"%APPDATA%\Microsoft\Windows\AccountPictures");


    /// <summary>
    /// Represents the Get Programs folder.
    /// Default path: <c>Not applicable—virtual folder</c>.
    /// </summary>
    public static KnownFolder AddNewPrograms { get; } = new("Get Programs", "VIRTUAL", new Guid(0xde61d971, 0x5ebc, 0x4f02, 0xa3, 0xa9, 0x6c, 0x82, 0x89, 0x5e, 0x5c, 0x04), @"Not applicable—virtual folder");


    /// <summary>
    /// Represents the Administrative Tools folder.
    /// Default path: <c>%APPDATA%\Microsoft\Windows\Start Menu\Programs\Administrative Tools</c>.
    /// </summary>
    public static KnownFolder AdminTools { get; } = new("Administrative Tools", "PERUSER", new Guid(0x724EF170, 0xA42D, 0x4FEF, 0x9F, 0x26, 0xB6, 0x0E, 0x84, 0x6F, 0xBA, 0x4F), @"%APPDATA%\Microsoft\Windows\Start Menu\Programs\Administrative Tools");


    /// <summary>
    /// Represents the AppDataDesktop folder.
    /// Default path: <c>%LOCALAPPDATA%\Desktop</c>.
    /// </summary>
    public static KnownFolder AppDataDesktop { get; } = new("AppDataDesktop", "PERUSER", new Guid(0xB2C5E279, 0x7ADD, 0x439F, 0xB2, 0x8C, 0xC4, 0x1F, 0xE1, 0xBB, 0xF6, 0x72), @"%LOCALAPPDATA%\Desktop");


    /// <summary>
    /// Represents the AppDataDocuments folder.
    /// Default path: <c>%LOCALAPPDATA%\Documents</c>.
    /// </summary>
    public static KnownFolder AppDataDocuments { get; } = new("AppDataDocuments", "PERUSER", new Guid(0x7BE16610, 0x1F7F, 0x44AC, 0xBF, 0xF0, 0x83, 0xE1, 0x5F, 0x2F, 0xFC, 0xA1), @"%LOCALAPPDATA%\Documents");


    /// <summary>
    /// Represents the AppDataFavorites folder.
    /// Default path: <c>%LOCALAPPDATA%\Favorites</c>.
    /// </summary>
    public static KnownFolder AppDataFavorites { get; } = new("AppDataFavorites", "PERUSER", new Guid(0x7CFBEFBC, 0xDE1F, 0x45AA, 0xB8, 0x43, 0xA5, 0x42, 0xAC, 0x53, 0x6C, 0xC9), @"%LOCALAPPDATA%\Favorites");


    /// <summary>
    /// Represents the AppDataProgramData folder.
    /// Default path: <c>%LOCALAPPDATA%\ProgramData</c>.
    /// </summary>
    public static KnownFolder AppDataProgramData { get; } = new("AppDataProgramData", "PERUSER", new Guid(0x559D40A3, 0xA036, 0x40FA, 0xAF, 0x61, 0x84, 0xCB, 0x43, 0x0A, 0x4D, 0x34), @"%LOCALAPPDATA%\ProgramData");


    /// <summary>
    /// Represents the Application Shortcuts folder.
    /// Default path: <c>%LOCALAPPDATA%\Microsoft\Windows\Application Shortcuts</c>.
    /// </summary>
    public static KnownFolder ApplicationShortcuts { get; } = new("Application Shortcuts", "PERUSER", new Guid(0xA3918781, 0xE5F2, 0x4890, 0xB3, 0xD9, 0xA7, 0xE5, 0x43, 0x32, 0x32, 0x8C), @"%LOCALAPPDATA%\Microsoft\Windows\Application Shortcuts");


    /// <summary>
    /// Represents the Applications folder.
    /// Default path: <c>Not applicable—virtual folder</c>.
    /// </summary>
    public static KnownFolder AppsFolder { get; } = new("Applications", "VIRTUAL", new Guid(0x1e87508d, 0x89c2, 0x42f0, 0x8a, 0x7e, 0x64, 0x5a, 0x0f, 0x50, 0xca, 0x58), @"Not applicable—virtual folder");


    /// <summary>
    /// Represents the Installed Updates folder.
    /// Default path: <c>Not applicable—virtual folder</c>.
    /// </summary>
    public static KnownFolder AppUpdates { get; } = new("Installed Updates", "VIRTUAL", new Guid(0xa305ce99, 0xf527, 0x492b, 0x8b, 0x1a, 0x7e, 0x76, 0xfa, 0x98, 0xd6, 0xe4), @"Not applicable—virtual folder");


    /// <summary>
    /// Represents the Camera Roll folder.
    /// Default path: <c>%USERPROFILE%\Pictures\Camera Roll</c>.
    /// </summary>
    public static KnownFolder CameraRoll { get; } = new("Camera Roll", "PERUSER", new Guid(0xAB5FB87B, 0x7CE2, 0x4F83, 0x91, 0x5D, 0x55, 0x08, 0x46, 0xC9, 0x53, 0x7B), @"%USERPROFILE%\Pictures\Camera Roll");


    /// <summary>
    /// Represents the Temporary Burn Folder folder.
    /// Default path: <c>%LOCALAPPDATA%\Microsoft\Windows\Burn\Burn</c>.
    /// </summary>
    public static KnownFolder CDBurning { get; } = new("Temporary Burn Folder", "PERUSER", new Guid(0x9E52AB10, 0xF80D, 0x49DF, 0xAC, 0xB8, 0x43, 0x30, 0xF5, 0x68, 0x78, 0x55), @"%LOCALAPPDATA%\Microsoft\Windows\Burn\Burn");


    /// <summary>
    /// Represents the Programs and Features folder.
    /// Default path: <c>Not applicable—virtual folder</c>.
    /// </summary>
    public static KnownFolder ChangeRemovePrograms { get; } = new("Programs and Features", "VIRTUAL", new Guid(0xdf7266ac, 0x9274, 0x4867, 0x8d, 0x55, 0x3b, 0xd6, 0x61, 0xde, 0x87, 0x2d), @"Not applicable—virtual folder");


    /// <summary>
    /// Represents the Administrative Tools folder.
    /// Default path: <c>%ALLUSERSPROFILE%\Microsoft\Windows\Start Menu\Programs\Administrative Tools</c>.
    /// </summary>
    public static KnownFolder CommonAdminTools { get; } = new("Administrative Tools", "COMMON", new Guid(0xD0384E7D, 0xBAC3, 0x4797, 0x8F, 0x14, 0xCB, 0xA2, 0x29, 0xB3, 0x92, 0xB5), @"%ALLUSERSPROFILE%\Microsoft\Windows\Start Menu\Programs\Administrative Tools");


    /// <summary>
    /// Represents the OEM Links folder.
    /// Default path: <c>%ALLUSERSPROFILE%\OEM Links</c>.
    /// </summary>
    public static KnownFolder CommonOEMLinks { get; } = new("OEM Links", "COMMON", new Guid(0xC1BAE2D0, 0x10DF, 0x4334, 0xBE, 0xDD, 0x7A, 0xA2, 0x0B, 0x22, 0x7A, 0x9D), @"%ALLUSERSPROFILE%\OEM Links");


    /// <summary>
    /// Represents the Programs folder.
    /// Default path: <c>%ALLUSERSPROFILE%\Microsoft\Windows\Start Menu\Programs</c>.
    /// </summary>
    public static KnownFolder CommonPrograms { get; } = new("Programs", "COMMON", new Guid(0x0139D44E, 0x6AFE, 0x49F2, 0x86, 0x90, 0x3D, 0xAF, 0xCA, 0xE6, 0xFF, 0xB8), @"%ALLUSERSPROFILE%\Microsoft\Windows\Start Menu\Programs");


    /// <summary>
    /// Represents the Start Menu folder.
    /// Default path: <c>%ALLUSERSPROFILE%\Microsoft\Windows\Start Menu</c>.
    /// </summary>
    public static KnownFolder CommonStartMenu { get; } = new("Start Menu", "COMMON", new Guid(0xA4115719, 0xD62E, 0x491D, 0xAA, 0x7C, 0xE7, 0x4B, 0x8B, 0xE3, 0xB0, 0x67), @"%ALLUSERSPROFILE%\Microsoft\Windows\Start Menu");


    /// <summary>
    /// Represents the Startup folder.
    /// Default path: <c>%ALLUSERSPROFILE%\Microsoft\Windows\Start Menu\Programs\StartUp</c>.
    /// </summary>
    public static KnownFolder CommonStartup { get; } = new("Startup", "COMMON", new Guid(0x82A5EA35, 0xD9CD, 0x47C5, 0x96, 0x29, 0xE1, 0x5D, 0x2F, 0x71, 0x4E, 0x6E), @"%ALLUSERSPROFILE%\Microsoft\Windows\Start Menu\Programs\StartUp");


    /// <summary>
    /// Represents the Templates folder.
    /// Default path: <c>%ALLUSERSPROFILE%\Microsoft\Windows\Templates</c>.
    /// </summary>
    public static KnownFolder CommonTemplates { get; } = new("Templates", "COMMON", new Guid(0xB94237E7, 0x57AC, 0x4347, 0x91, 0x51, 0xB0, 0x8C, 0x6C, 0x32, 0xD1, 0xF7), @"%ALLUSERSPROFILE%\Microsoft\Windows\Templates");


    /// <summary>
    /// Represents the Computer folder.
    /// Default path: <c>Not applicable—virtual folder</c>.
    /// </summary>
    public static KnownFolder ComputerFolder { get; } = new("Computer", "VIRTUAL", new Guid(0x0AC0837C, 0xBBF8, 0x452A, 0x85, 0x0D, 0x79, 0xD0, 0x8E, 0x66, 0x7C, 0xA7), @"Not applicable—virtual folder");


    /// <summary>
    /// Represents the Conflicts folder.
    /// Default path: <c>Not applicable—virtual folder</c>.
    /// </summary>
    public static KnownFolder ConflictFolder { get; } = new("Conflicts", "VIRTUAL", new Guid(0x4bfefb45, 0x347d, 0x4006, 0xa5, 0xbe, 0xac, 0x0c, 0xb0, 0x56, 0x71, 0x92), @"Not applicable—virtual folder");


    /// <summary>
    /// Represents the Network Connections folder.
    /// Default path: <c>Not applicable—virtual folder</c>.
    /// </summary>
    public static KnownFolder ConnectionsFolder { get; } = new("Network Connections", "VIRTUAL", new Guid(0x6F0CD92B, 0x2E97, 0x45D1, 0x88, 0xFF, 0xB0, 0xD1, 0x86, 0xB8, 0xDE, 0xDD), @"Not applicable—virtual folder");


    /// <summary>
    /// Represents the Contacts folder.
    /// Default path: <c>%USERPROFILE%\Contacts</c>.
    /// </summary>
    public static KnownFolder Contacts { get; } = new("Contacts", "PERUSER", new Guid(0x56784854, 0xC6CB, 0x462b, 0x81, 0x69, 0x88, 0xE3, 0x50, 0xAC, 0xB8, 0x82), @"%USERPROFILE%\Contacts");


    /// <summary>
    /// Represents the Control Panel folder.
    /// Default path: <c>Not applicable—virtual folder</c>.
    /// </summary>
    public static KnownFolder ControlPanelFolder { get; } = new("Control Panel", "VIRTUAL", new Guid(0x82A74AEB, 0xAEB4, 0x465C, 0xA0, 0x14, 0xD0, 0x97, 0xEE, 0x34, 0x6D, 0x63), @"Not applicable—virtual folder");


    /// <summary>
    /// Represents the Cookies folder.
    /// Default path: <c>%APPDATA%\Microsoft\Windows\Cookies</c>.
    /// </summary>
    public static KnownFolder Cookies { get; } = new("Cookies", "PERUSER", new Guid(0x2B0F765D, 0xC0E9, 0x4171, 0x90, 0x8E, 0x08, 0xA6, 0x11, 0xB8, 0x4F, 0xF6), @"%APPDATA%\Microsoft\Windows\Cookies");


    /// <summary>
    /// Represents the Desktop folder.
    /// Default path: <c>%USERPROFILE%\Desktop</c>.
    /// </summary>
    public static KnownFolder Desktop { get; } = new("Desktop", "PERUSER", new Guid(0xB4BFCC3A, 0xDB2C, 0x424C, 0xB0, 0x29, 0x7F, 0xE9, 0x9A, 0x87, 0xC6, 0x41), @"%USERPROFILE%\Desktop");


    /// <summary>
    /// Represents the DeviceMetadataStore folder.
    /// Default path: <c>%ALLUSERSPROFILE%\Microsoft\Windows\DeviceMetadataStore</c>.
    /// </summary>
    public static KnownFolder DeviceMetadataStore { get; } = new("DeviceMetadataStore", "COMMON", new Guid(0x5CE4A5E9, 0xE4EB, 0x479D, 0xB8, 0x9F, 0x13, 0x0C, 0x02, 0x88, 0x61, 0x55), @"%ALLUSERSPROFILE%\Microsoft\Windows\DeviceMetadataStore");


    /// <summary>
    /// Represents the Documents folder.
    /// Default path: <c>%USERPROFILE%\Documents</c>.
    /// </summary>
    public static KnownFolder Documents { get; } = new("Documents", "PERUSER", new Guid(0xFDD39AD0, 0x238F, 0x46AF, 0xAD, 0xB4, 0x6C, 0x85, 0x48, 0x03, 0x69, 0xC7), @"%USERPROFILE%\Documents");


    /// <summary>
    /// Represents the Documents folder.
    /// Default path: <c>%APPDATA%\Microsoft\Windows\Libraries\Documents.library-ms</c>.
    /// </summary>
    public static KnownFolder DocumentsLibrary { get; } = new("Documents", "PERUSER", new Guid(0x7B0DB17D, 0x9CD2, 0x4A93, 0x97, 0x33, 0x46, 0xCC, 0x89, 0x02, 0x2E, 0x7C), @"%APPDATA%\Microsoft\Windows\Libraries\Documents.library-ms");


    /// <summary>
    /// Represents the Downloads folder.
    /// Default path: <c>%USERPROFILE%\Downloads</c>.
    /// </summary>
    public static KnownFolder Downloads { get; } = new("Downloads", "PERUSER", new Guid(0x374DE290, 0x123F, 0x4565, 0x91, 0x64, 0x39, 0xC4, 0x92, 0x5E, 0x46, 0x7B), @"%USERPROFILE%\Downloads");


    /// <summary>
    /// Represents the Favorites folder.
    /// Default path: <c>%USERPROFILE%\Favorites</c>.
    /// </summary>
    public static KnownFolder Favorites { get; } = new("Favorites", "PERUSER", new Guid(0x1777F761, 0x68AD, 0x4D8A, 0x87, 0xBD, 0x30, 0xB7, 0x59, 0xFA, 0x33, 0xDD), @"%USERPROFILE%\Favorites");


    /// <summary>
    /// Represents the Fonts folder.
    /// Default path: <c>%windir%\Fonts</c>.
    /// </summary>
    public static KnownFolder Fonts { get; } = new("Fonts", "FIXED", new Guid(0xFD228CB7, 0xAE11, 0x4AE3, 0x86, 0x4C, 0x16, 0xF3, 0x91, 0x0A, 0xB8, 0xFE), @"%windir%\Fonts");


    /// <summary>
    /// Represents the Games folder.
    /// Default path: <c>Not applicable—virtual folder</c>.
    /// </summary>
    public static KnownFolder Games { get; } = new("Games", "VIRTUAL", new Guid(0xCAC52C1A, 0xB53D, 0x4edc, 0x92, 0xD7, 0x6B, 0x2E, 0x8A, 0xC1, 0x94, 0x34), @"Not applicable—virtual folder");


    /// <summary>
    /// Represents the GameExplorer folder.
    /// Default path: <c>%LOCALAPPDATA%\Microsoft\Windows\GameExplorer</c>.
    /// </summary>
    public static KnownFolder GameTasks { get; } = new("GameExplorer", "PERUSER", new Guid(0x054FAE61, 0x4DD8, 0x4787, 0x80, 0xB6, 0x09, 0x02, 0x20, 0xC4, 0xB7, 0x00), @"%LOCALAPPDATA%\Microsoft\Windows\GameExplorer");


    /// <summary>
    /// Represents the History folder.
    /// Default path: <c>%LOCALAPPDATA%\Microsoft\Windows\History</c>.
    /// </summary>
    public static KnownFolder History { get; } = new("History", "PERUSER", new Guid(0xD9DC8A3B, 0xB784, 0x432E, 0xA7, 0x81, 0x5A, 0x11, 0x30, 0xA7, 0x59, 0x63), @"%LOCALAPPDATA%\Microsoft\Windows\History");


    /// <summary>
    /// Represents the Homegroup folder.
    /// Default path: <c>Not applicable—virtual folder</c>.
    /// </summary>
    public static KnownFolder HomeGroup { get; } = new("Homegroup", "VIRTUAL", new Guid(0x52528A6B, 0xB9E3, 0x4ADD, 0xB6, 0x0D, 0x58, 0x8C, 0x2D, 0xBA, 0x84, 0x2D), @"Not applicable—virtual folder");


    /// <summary>
    /// Represents the The user's username (%USERNAME%) folder.
    /// Default path: <c>Not applicable—virtual folder</c>.
    /// </summary>
    public static KnownFolder HomeGroupCurrentUser { get; } = new("The user's username (%USERNAME%)", "VIRTUAL", new Guid(0x9B74B6A3, 0x0DFD, 0x4f11, 0x9E, 0x78, 0x5F, 0x78, 0x00, 0xF2, 0xE7, 0x72), @"Not applicable—virtual folder");


    /// <summary>
    /// Represents the ImplicitAppShortcuts folder.
    /// Default path: <c>%APPDATA%\Microsoft\Internet Explorer\Quick Launch\User Pinned\ImplicitAppShortcuts</c>.
    /// </summary>
    public static KnownFolder ImplicitAppShortcuts { get; } = new("ImplicitAppShortcuts", "PERUSER", new Guid(0xBCB5256F, 0x79F6, 0x4CEE, 0xB7, 0x25, 0xDC, 0x34, 0xE4, 0x02, 0xFD, 0x46), @"%APPDATA%\Microsoft\Internet Explorer\Quick Launch\User Pinned\ImplicitAppShortcuts");


    /// <summary>
    /// Represents the Temporary Internet Files folder.
    /// Default path: <c>%LOCALAPPDATA%\Microsoft\Windows\Temporary Internet Files</c>.
    /// </summary>
    public static KnownFolder InternetCache { get; } = new("Temporary Internet Files", "PERUSER", new Guid(0x352481E8, 0x33BE, 0x4251, 0xBA, 0x85, 0x60, 0x07, 0xCA, 0xED, 0xCF, 0x9D), @"%LOCALAPPDATA%\Microsoft\Windows\Temporary Internet Files");


    /// <summary>
    /// Represents the The Internet folder.
    /// Default path: <c>Not applicable—virtual folder</c>.
    /// </summary>
    public static KnownFolder InternetFolder { get; } = new("The Internet", "VIRTUAL", new Guid(0x4D9F7874, 0x4E0C, 0x4904, 0x96, 0x7B, 0x40, 0xB0, 0xD2, 0x0C, 0x3E, 0x4B), @"Not applicable—virtual folder");


    /// <summary>
    /// Represents the Libraries folder.
    /// Default path: <c>%APPDATA%\Microsoft\Windows\Libraries</c>.
    /// </summary>
    public static KnownFolder Libraries { get; } = new("Libraries", "PERUSER", new Guid(0x1B3EA5DC, 0xB587, 0x4786, 0xB4, 0xEF, 0xBD, 0x1D, 0xC3, 0x32, 0xAE, 0xAE), @"%APPDATA%\Microsoft\Windows\Libraries");


    /// <summary>
    /// Represents the Links folder.
    /// Default path: <c>%USERPROFILE%\Links</c>.
    /// </summary>
    public static KnownFolder Links { get; } = new("Links", "PERUSER", new Guid(0xbfb9d5e0, 0xc6a9, 0x404c, 0xb2, 0xb2, 0xae, 0x6d, 0xb6, 0xaf, 0x49, 0x68), @"%USERPROFILE%\Links");


    /// <summary>
    /// Represents the Local folder.
    /// Default path: <c>%LOCALAPPDATA% (%USERPROFILE%\AppData\Local)</c>.
    /// </summary>
    public static KnownFolder LocalAppData { get; } = new("Local", "PERUSER", new Guid(0xF1B32785, 0x6FBA, 0x4FCF, 0x9D, 0x55, 0x7B, 0x8E, 0x7F, 0x15, 0x70, 0x91), @"%LOCALAPPDATA% (%USERPROFILE%\AppData\Local)");


    /// <summary>
    /// Represents the LocalLow folder.
    /// Default path: <c>%USERPROFILE%\AppData\LocalLow</c>.
    /// </summary>
    public static KnownFolder LocalAppDataLow { get; } = new("LocalLow", "PERUSER", new Guid(0xA520A1A4, 0x1780, 0x4FF6, 0xBD, 0x18, 0x16, 0x73, 0x43, 0xC5, 0xAF, 0x16), @"%USERPROFILE%\AppData\LocalLow");


    /// <summary>
    /// Represents the None folder.
    /// Default path: <c>%windir%\resources\0409 (code page)</c>.
    /// </summary>
    public static KnownFolder LocalizedResourcesDir { get; } = new("None", "FIXED", new Guid(0x2A00375E, 0x224C, 0x49DE, 0xB8, 0xD1, 0x44, 0x0D, 0xF7, 0xEF, 0x3D, 0xDC), @"%windir%\resources\0409 (code page)");


    /// <summary>
    /// Represents the Music folder.
    /// Default path: <c>%USERPROFILE%\Music</c>.
    /// </summary>
    public static KnownFolder Music { get; } = new("Music", "PERUSER", new Guid(0x4BD8D571, 0x6D19, 0x48D3, 0xBE, 0x97, 0x42, 0x22, 0x20, 0x08, 0x0E, 0x43), @"%USERPROFILE%\Music");


    /// <summary>
    /// Represents the Music folder.
    /// Default path: <c>%APPDATA%\Microsoft\Windows\Libraries\Music.library-ms</c>.
    /// </summary>
    public static KnownFolder MusicLibrary { get; } = new("Music", "PERUSER", new Guid(0x2112AB0A, 0xC86A, 0x4FFE, 0xA3, 0x68, 0x0D, 0xE9, 0x6E, 0x47, 0x01, 0x2E), @"%APPDATA%\Microsoft\Windows\Libraries\Music.library-ms");


    /// <summary>
    /// Represents the Network Shortcuts folder.
    /// Default path: <c>%APPDATA%\Microsoft\Windows\Network Shortcuts</c>.
    /// </summary>
    public static KnownFolder NetHood { get; } = new("Network Shortcuts", "PERUSER", new Guid(0xC5ABBF53, 0xE17F, 0x4121, 0x89, 0x00, 0x86, 0x62, 0x6F, 0xC2, 0xC9, 0x73), @"%APPDATA%\Microsoft\Windows\Network Shortcuts");


    /// <summary>
    /// Represents the Network folder.
    /// Default path: <c>Not applicable—virtual folder</c>.
    /// </summary>
    public static KnownFolder NetworkFolder { get; } = new("Network", "VIRTUAL", new Guid(0xD20BEEC4, 0x5CA8, 0x4905, 0xAE, 0x3B, 0xBF, 0x25, 0x1E, 0xA0, 0x9B, 0x53), @"Not applicable—virtual folder");


    /// <summary>
    /// Represents the 3D Objects folder.
    /// Default path: <c>%USERPROFILE%\3D Objects</c>.
    /// </summary>
    public static KnownFolder Objects3D { get; } = new("3D Objects", "PERUSER", new Guid(0x31C0DD25, 0x9439, 0x4F12, 0xBF, 0x41, 0x7F, 0xF4, 0xED, 0xA3, 0x87, 0x22), @"%USERPROFILE%\3D Objects");


    /// <summary>
    /// Represents the Original Images folder.
    /// Default path: <c>%LOCALAPPDATA%\Microsoft\Windows Photo Gallery\Original Images</c>.
    /// </summary>
    public static KnownFolder OriginalImages { get; } = new("Original Images", "PERUSER", new Guid(0x2C36C0AA, 0x5812, 0x4b87, 0xBF, 0xD0, 0x4C, 0xD0, 0xDF, 0xB1, 0x9B, 0x39), @"%LOCALAPPDATA%\Microsoft\Windows Photo Gallery\Original Images");


    /// <summary>
    /// Represents the Slide Shows folder.
    /// Default path: <c>%USERPROFILE%\Pictures\Slide Shows</c>.
    /// </summary>
    public static KnownFolder PhotoAlbums { get; } = new("Slide Shows", "PERUSER", new Guid(0x69D2CF90, 0xFC33, 0x4FB7, 0x9A, 0x0C, 0xEB, 0xB0, 0xF0, 0xFC, 0xB4, 0x3C), @"%USERPROFILE%\Pictures\Slide Shows");


    /// <summary>
    /// Represents the Pictures folder.
    /// Default path: <c>%APPDATA%\Microsoft\Windows\Libraries\Pictures.library-ms</c>.
    /// </summary>
    public static KnownFolder PicturesLibrary { get; } = new("Pictures", "PERUSER", new Guid(0xA990AE9F, 0xA03B, 0x4E80, 0x94, 0xBC, 0x99, 0x12, 0xD7, 0x50, 0x41, 0x04), @"%APPDATA%\Microsoft\Windows\Libraries\Pictures.library-ms");


    /// <summary>
    /// Represents the Pictures folder.
    /// Default path: <c>%USERPROFILE%\Pictures</c>.
    /// </summary>
    public static KnownFolder Pictures { get; } = new("Pictures", "PERUSER", new Guid(0x33E28130, 0x4E1E, 0x4676, 0x83, 0x5A, 0x98, 0x39, 0x5C, 0x3B, 0xC3, 0xBB), @"%USERPROFILE%\Pictures");


    /// <summary>
    /// Represents the Playlists folder.
    /// Default path: <c>%USERPROFILE%\Music\Playlists</c>.
    /// </summary>
    public static KnownFolder Playlists { get; } = new("Playlists", "PERUSER", new Guid(0xDE92C1C7, 0x837F, 0x4F69, 0xA3, 0xBB, 0x86, 0xE6, 0x31, 0x20, 0x4A, 0x23), @"%USERPROFILE%\Music\Playlists");


    /// <summary>
    /// Represents the Printers folder.
    /// Default path: <c>Not applicable—virtual folder</c>.
    /// </summary>
    public static KnownFolder PrintersFolder { get; } = new("Printers", "VIRTUAL", new Guid(0x76FC4E2D, 0xD6AD, 0x4519, 0xA6, 0x63, 0x37, 0xBD, 0x56, 0x06, 0x81, 0x85), @"Not applicable—virtual folder");


    /// <summary>
    /// Represents the Printer Shortcuts folder.
    /// Default path: <c>%APPDATA%\Microsoft\Windows\Printer Shortcuts</c>.
    /// </summary>
    public static KnownFolder PrintHood { get; } = new("Printer Shortcuts", "PERUSER", new Guid(0x9274BD8D, 0xCFD1, 0x41C3, 0xB3, 0x5E, 0xB1, 0x3F, 0x55, 0xA7, 0x58, 0xF4), @"%APPDATA%\Microsoft\Windows\Printer Shortcuts");


    /// <summary>
    /// Represents the The user's username (%USERNAME%) folder.
    /// Default path: <c>%USERPROFILE% (%SystemDrive%\Users\%USERNAME%)</c>.
    /// </summary>
    public static KnownFolder Profile { get; } = new("The user's username (%USERNAME%)", "FIXED", new Guid(0x5E6C858F, 0x0E22, 0x4760, 0x9A, 0xFE, 0xEA, 0x33, 0x17, 0xB6, 0x71, 0x73), @"%USERPROFILE% (%SystemDrive%\Users\%USERNAME%)");


    /// <summary>
    /// Represents the ProgramData folder.
    /// Default path: <c>%ALLUSERSPROFILE% (%ProgramData%, %SystemDrive%\ProgramData)</c>.
    /// </summary>
    public static KnownFolder ProgramData { get; } = new("ProgramData", "FIXED", new Guid(0x62AB5D82, 0xFDC1, 0x4DC3, 0xA9, 0xDD, 0x07, 0x0D, 0x1D, 0x49, 0x5D, 0x97), @"%ALLUSERSPROFILE% (%ProgramData%, %SystemDrive%\ProgramData)");


    /// <summary>
    /// Represents the Program Files folder.
    /// Default path: <c>%ProgramFiles% (%SystemDrive%\Program Files)</c>.
    /// </summary>
    public static KnownFolder ProgramFiles { get; } = new("Program Files", "FIXED", new Guid(0x905e63b6, 0xc1bf, 0x494e, 0xb2, 0x9c, 0x65, 0xb7, 0x32, 0xd3, 0xd2, 0x1a), @"%ProgramFiles% (%SystemDrive%\Program Files)");


    /// <summary>
    /// Represents the Program Files folder.
    /// Default path: <c>%ProgramFiles% (%SystemDrive%\Program Files)</c>.
    /// </summary>
    public static KnownFolder ProgramFilesX64 { get; } = new("Program Files", "FIXED", new Guid(0x6D809377, 0x6AF0, 0x444b, 0x89, 0x57, 0xA3, 0x77, 0x3F, 0x02, 0x20, 0x0E), @"%ProgramFiles% (%SystemDrive%\Program Files)");


    /// <summary>
    /// Represents the Program Files folder.
    /// Default path: <c>%ProgramFiles% (%SystemDrive%\Program Files)</c>.
    /// </summary>
    public static KnownFolder ProgramFilesX86 { get; } = new("Program Files", "FIXED", new Guid(0x7C5A40EF, 0xA0FB, 0x4BFC, 0x87, 0x4A, 0xC0, 0xF2, 0xE0, 0xB9, 0xFA, 0x8E), @"%ProgramFiles% (%SystemDrive%\Program Files)");


    /// <summary>
    /// Represents the Common Files folder.
    /// Default path: <c>%ProgramFiles%\Common Files</c>.
    /// </summary>
    public static KnownFolder ProgramFilesCommon { get; } = new("Common Files", "FIXED", new Guid(0xF7F1ED05, 0x9F6D, 0x47A2, 0xAA, 0xAE, 0x29, 0xD3, 0x17, 0xC6, 0xF0, 0x66), @"%ProgramFiles%\Common Files");


    /// <summary>
    /// Represents the Common Files folder.
    /// Default path: <c>%ProgramFiles%\Common Files</c>.
    /// </summary>
    public static KnownFolder ProgramFilesCommonX64 { get; } = new("Common Files", "FIXED", new Guid(0x6365D5A7, 0x0F0D, 0x45E5, 0x87, 0xF6, 0x0D, 0xA5, 0x6B, 0x6A, 0x4F, 0x7D), @"%ProgramFiles%\Common Files");


    /// <summary>
    /// Represents the Common Files folder.
    /// Default path: <c>%ProgramFiles%\Common Files</c>.
    /// </summary>
    public static KnownFolder ProgramFilesCommonX86 { get; } = new("Common Files", "FIXED", new Guid(0xDE974D24, 0xD9C6, 0x4D3E, 0xBF, 0x91, 0xF4, 0x45, 0x51, 0x20, 0xB9, 0x17), @"%ProgramFiles%\Common Files");


    /// <summary>
    /// Represents the Programs folder.
    /// Default path: <c>%APPDATA%\Microsoft\Windows\Start Menu\Programs</c>.
    /// </summary>
    public static KnownFolder Programs { get; } = new("Programs", "PERUSER", new Guid(0xA77F5D77, 0x2E2B, 0x44C3, 0xA6, 0xA2, 0xAB, 0xA6, 0x01, 0x05, 0x4A, 0x51), @"%APPDATA%\Microsoft\Windows\Start Menu\Programs");


    /// <summary>
    /// Represents the Public folder.
    /// Default path: <c>%PUBLIC% (%SystemDrive%\Users\Public)</c>.
    /// </summary>
    public static KnownFolder Public { get; } = new("Public", "FIXED", new Guid(0xDFDF76A2, 0xC82A, 0x4D63, 0x90, 0x6A, 0x56, 0x44, 0xAC, 0x45, 0x73, 0x85), @"%PUBLIC% (%SystemDrive%\Users\Public)");


    /// <summary>
    /// Represents the Public Desktop folder.
    /// Default path: <c>%PUBLIC%\Desktop</c>.
    /// </summary>
    public static KnownFolder PublicDesktop { get; } = new("Public Desktop", "COMMON", new Guid(0xC4AA340D, 0xF20F, 0x4863, 0xAF, 0xEF, 0xF8, 0x7E, 0xF2, 0xE6, 0xBA, 0x25), @"%PUBLIC%\Desktop");


    /// <summary>
    /// Represents the Public Documents folder.
    /// Default path: <c>%PUBLIC%\Documents</c>.
    /// </summary>
    public static KnownFolder PublicDocuments { get; } = new("Public Documents", "COMMON", new Guid(0xED4824AF, 0xDCE4, 0x45A8, 0x81, 0xE2, 0xFC, 0x79, 0x65, 0x08, 0x36, 0x34), @"%PUBLIC%\Documents");


    /// <summary>
    /// Represents the Public Downloads folder.
    /// Default path: <c>%PUBLIC%\Downloads</c>.
    /// </summary>
    public static KnownFolder PublicDownloads { get; } = new("Public Downloads", "COMMON", new Guid(0x3D644C9B, 0x1FB8, 0x4f30, 0x9B, 0x45, 0xF6, 0x70, 0x23, 0x5F, 0x79, 0xC0), @"%PUBLIC%\Downloads");


    /// <summary>
    /// Represents the GameExplorer folder.
    /// Default path: <c>%ALLUSERSPROFILE%\Microsoft\Windows\GameExplorer</c>.
    /// </summary>
    public static KnownFolder PublicGameTasks { get; } = new("GameExplorer", "COMMON", new Guid(0xDEBF2536, 0xE1A8, 0x4c59, 0xB6, 0xA2, 0x41, 0x45, 0x86, 0x47, 0x6A, 0xEA), @"%ALLUSERSPROFILE%\Microsoft\Windows\GameExplorer");


    /// <summary>
    /// Represents the Libraries folder.
    /// Default path: <c>%ALLUSERSPROFILE%\Microsoft\Windows\Libraries</c>.
    /// </summary>
    public static KnownFolder PublicLibraries { get; } = new("Libraries", "COMMON", new Guid(0x48DAF80B, 0xE6CF, 0x4F4E, 0xB8, 0x00, 0x0E, 0x69, 0xD8, 0x4E, 0xE3, 0x84), @"%ALLUSERSPROFILE%\Microsoft\Windows\Libraries");


    /// <summary>
    /// Represents the Public Music folder.
    /// Default path: <c>%PUBLIC%\Music</c>.
    /// </summary>
    public static KnownFolder PublicMusic { get; } = new("Public Music", "COMMON", new Guid(0x3214FAB5, 0x9757, 0x4298, 0xBB, 0x61, 0x92, 0xA9, 0xDE, 0xAA, 0x44, 0xFF), @"%PUBLIC%\Music");


    /// <summary>
    /// Represents the Public Pictures folder.
    /// Default path: <c>%PUBLIC%\Pictures</c>.
    /// </summary>
    public static KnownFolder PublicPictures { get; } = new("Public Pictures", "COMMON", new Guid(0xB6EBFB86, 0x6907, 0x413C, 0x9A, 0xF7, 0x4F, 0xC2, 0xAB, 0xF0, 0x7C, 0xC5), @"%PUBLIC%\Pictures");


    /// <summary>
    /// Represents the Ringtones folder.
    /// Default path: <c>%ALLUSERSPROFILE%\Microsoft\Windows\Ringtones</c>.
    /// </summary>
    public static KnownFolder PublicRingtones { get; } = new("Ringtones", "COMMON", new Guid(0xE555AB60, 0x153B, 0x4D17, 0x9F, 0x04, 0xA5, 0xFE, 0x99, 0xFC, 0x15, 0xEC), @"%ALLUSERSPROFILE%\Microsoft\Windows\Ringtones");


    /// <summary>
    /// Represents the Public Account Pictures folder.
    /// Default path: <c>%PUBLIC%\AccountPictures</c>.
    /// </summary>
    public static KnownFolder PublicUserTiles { get; } = new("Public Account Pictures", "COMMON", new Guid(0x0482af6c, 0x08f1, 0x4c34, 0x8c, 0x90, 0xe1, 0x7e, 0xc9, 0x8b, 0x1e, 0x17), @"%PUBLIC%\AccountPictures");


    /// <summary>
    /// Represents the Public Videos folder.
    /// Default path: <c>%PUBLIC%\Videos</c>.
    /// </summary>
    public static KnownFolder PublicVideos { get; } = new("Public Videos", "COMMON", new Guid(0x2400183A, 0x6185, 0x49FB, 0xA2, 0xD8, 0x4A, 0x39, 0x2A, 0x60, 0x2B, 0xA3), @"%PUBLIC%\Videos");


    /// <summary>
    /// Represents the Quick Launch folder.
    /// Default path: <c>%APPDATA%\Microsoft\Internet Explorer\Quick Launch</c>.
    /// </summary>
    public static KnownFolder QuickLaunch { get; } = new("Quick Launch", "PERUSER", new Guid(0x52a4f021, 0x7b75, 0x48a9, 0x9f, 0x6b, 0x4b, 0x87, 0xa2, 0x10, 0xbc, 0x8f), @"%APPDATA%\Microsoft\Internet Explorer\Quick Launch");


    /// <summary>
    /// Represents the Recent Items folder.
    /// Default path: <c>%APPDATA%\Microsoft\Windows\Recent</c>.
    /// </summary>
    public static KnownFolder Recent { get; } = new("Recent Items", "PERUSER", new Guid(0xAE50C081, 0xEBD2, 0x438A, 0x86, 0x55, 0x8A, 0x09, 0x2E, 0x34, 0x98, 0x7A), @"%APPDATA%\Microsoft\Windows\Recent");


    /// <summary>
    /// Represents the Recorded TV folder.
    /// Default path: <c>%PUBLIC%\RecordedTV.library-ms</c>.
    /// </summary>
    public static KnownFolder RecordedTVLibrary { get; } = new("Recorded TV", "COMMON", new Guid(0x1A6FDBA2, 0xF42D, 0x4358, 0xA7, 0x98, 0xB7, 0x4D, 0x74, 0x59, 0x26, 0xC5), @"%PUBLIC%\RecordedTV.library-ms");


    /// <summary>
    /// Represents the Recycle Bin folder.
    /// Default path: <c>Not applicable—virtual folder</c>.
    /// </summary>
    public static KnownFolder RecycleBinFolder { get; } = new("Recycle Bin", "VIRTUAL", new Guid(0xB7534046, 0x3ECB, 0x4C18, 0xBE, 0x4E, 0x64, 0xCD, 0x4C, 0xB7, 0xD6, 0xAC), @"Not applicable—virtual folder");


    /// <summary>
    /// Represents the Resources folder.
    /// Default path: <c>%windir%\Resources</c>.
    /// </summary>
    public static KnownFolder ResourceDir { get; } = new("Resources", "FIXED", new Guid(0x8AD10C31, 0x2ADB, 0x4296, 0xA8, 0xF7, 0xE4, 0x70, 0x12, 0x32, 0xC9, 0x72), @"%windir%\Resources");


    /// <summary>
    /// Represents the Ringtones folder.
    /// Default path: <c>%LOCALAPPDATA%\Microsoft\Windows\Ringtones</c>.
    /// </summary>
    public static KnownFolder Ringtones { get; } = new("Ringtones", "PERUSER", new Guid(0xC870044B, 0xF49E, 0x4126, 0xA9, 0xC3, 0xB5, 0x2A, 0x1F, 0xF4, 0x11, 0xE8), @"%LOCALAPPDATA%\Microsoft\Windows\Ringtones");


    /// <summary>
    /// Represents the Roaming folder.
    /// Default path: <c>%APPDATA% (%USERPROFILE%\AppData\Roaming)</c>.
    /// </summary>
    public static KnownFolder RoamingAppData { get; } = new("Roaming", "PERUSER", new Guid(0x3EB685DB, 0x65F9, 0x4CF6, 0xA0, 0x3A, 0xE3, 0xEF, 0x65, 0x72, 0x9F, 0x3D), @"%APPDATA% (%USERPROFILE%\AppData\Roaming)");


    /// <summary>
    /// Represents the RoamedTileImages folder.
    /// Default path: <c>%LOCALAPPDATA%\Microsoft\Windows\RoamedTileImages</c>.
    /// </summary>
    public static KnownFolder RoamedTileImages { get; } = new("RoamedTileImages", "PERUSER", new Guid(0xAAA8D5A5, 0xF1D6, 0x4259, 0xBA, 0xA8, 0x78, 0xE7, 0xEF, 0x60, 0x83, 0x5E), @"%LOCALAPPDATA%\Microsoft\Windows\RoamedTileImages");


    /// <summary>
    /// Represents the RoamingTiles folder.
    /// Default path: <c>%LOCALAPPDATA%\Microsoft\Windows\RoamingTiles</c>.
    /// </summary>
    public static KnownFolder RoamingTiles { get; } = new("RoamingTiles", "PERUSER", new Guid(0x00BCFC5A, 0xED94, 0x4e48, 0x96, 0xA1, 0x3F, 0x62, 0x17, 0xF2, 0x19, 0x90), @"%LOCALAPPDATA%\Microsoft\Windows\RoamingTiles");


    /// <summary>
    /// Represents the Sample Music folder.
    /// Default path: <c>%PUBLIC%\Music\Sample Music</c>.
    /// </summary>
    public static KnownFolder SampleMusic { get; } = new("Sample Music", "COMMON", new Guid(0xB250C668, 0xF57D, 0x4EE1, 0xA6, 0x3C, 0x29, 0x0E, 0xE7, 0xD1, 0xAA, 0x1F), @"%PUBLIC%\Music\Sample Music");


    /// <summary>
    /// Represents the Sample Pictures folder.
    /// Default path: <c>%PUBLIC%\Pictures\Sample Pictures</c>.
    /// </summary>
    public static KnownFolder SamplePictures { get; } = new("Sample Pictures", "COMMON", new Guid(0xC4900540, 0x2379, 0x4C75, 0x84, 0x4B, 0x64, 0xE6, 0xFA, 0xF8, 0x71, 0x6B), @"%PUBLIC%\Pictures\Sample Pictures");


    /// <summary>
    /// Represents the Sample Playlists folder.
    /// Default path: <c>%PUBLIC%\Music\Sample Playlists</c>.
    /// </summary>
    public static KnownFolder SamplePlaylists { get; } = new("Sample Playlists", "COMMON", new Guid(0x15CA69B3, 0x30EE, 0x49C1, 0xAC, 0xE1, 0x6B, 0x5E, 0xC3, 0x72, 0xAF, 0xB5), @"%PUBLIC%\Music\Sample Playlists");


    /// <summary>
    /// Represents the Sample Videos folder.
    /// Default path: <c>%PUBLIC%\Videos\Sample Videos</c>.
    /// </summary>
    public static KnownFolder SampleVideos { get; } = new("Sample Videos", "COMMON", new Guid(0x859EAD94, 0x2E85, 0x48AD, 0xA7, 0x1A, 0x09, 0x69, 0xCB, 0x56, 0xA6, 0xCD), @"%PUBLIC%\Videos\Sample Videos");


    /// <summary>
    /// Represents the Saved Games folder.
    /// Default path: <c>%USERPROFILE%\Saved Games</c>.
    /// </summary>
    public static KnownFolder SavedGames { get; } = new("Saved Games", "PERUSER", new Guid(0x4C5C32FF, 0xBB9D, 0x43b0, 0xB5, 0xB4, 0x2D, 0x72, 0xE5, 0x4E, 0xAA, 0xA4), @"%USERPROFILE%\Saved Games");


    /// <summary>
    /// Represents the Saved Pictures folder.
    /// Default path: <c>%USERPROFILE%\Pictures\Saved Pictures</c>.
    /// </summary>
    public static KnownFolder SavedPictures { get; } = new("Saved Pictures", "PERUSER", new Guid(0x3B193882, 0xD3AD, 0x4eab, 0x96, 0x5A, 0x69, 0x82, 0x9D, 0x1F, 0xB5, 0x9F), @"%USERPROFILE%\Pictures\Saved Pictures");


    /// <summary>
    /// Represents the Saved Pictures Library folder.
    /// Default path: <c>%APPDATA%\Microsoft\Windows\Libraries\SavedPictures.library-ms</c>.
    /// </summary>
    public static KnownFolder SavedPicturesLibrary { get; } = new("Saved Pictures Library", "PERUSER", new Guid(0xE25B5812, 0xBE88, 0x4bd9, 0x94, 0xB0, 0x29, 0x23, 0x34, 0x77, 0xB6, 0xC3), @"%APPDATA%\Microsoft\Windows\Libraries\SavedPictures.library-ms");


    /// <summary>
    /// Represents the Searches folder.
    /// Default path: <c>%USERPROFILE%\Searches</c>.
    /// </summary>
    public static KnownFolder SavedSearches { get; } = new("Searches", "PERUSER", new Guid(0x7d1d3a04, 0xdebb, 0x4115, 0x95, 0xcf, 0x2f, 0x29, 0xda, 0x29, 0x20, 0xda), @"%USERPROFILE%\Searches");


    /// <summary>
    /// Represents the Screenshots folder.
    /// Default path: <c>%USERPROFILE%\Pictures\Screenshots</c>.
    /// </summary>
    public static KnownFolder Screenshots { get; } = new("Screenshots", "PERUSER", new Guid(0xb7bede81, 0xdf94, 0x4682, 0xa7, 0xd8, 0x57, 0xa5, 0x26, 0x20, 0xb8, 0x6f), @"%USERPROFILE%\Pictures\Screenshots");


    /// <summary>
    /// Represents the Offline Files folder.
    /// Default path: <c>Not applicable—virtual folder</c>.
    /// </summary>
    public static KnownFolder SEARCH_CSC { get; } = new("Offline Files", "VIRTUAL", new Guid(0xee32e446, 0x31ca, 0x4aba, 0x81, 0x4f, 0xa5, 0xeb, 0xd2, 0xfd, 0x6d, 0x5e), @"Not applicable—virtual folder");


    /// <summary>
    /// Represents the History folder.
    /// Default path: <c>%LOCALAPPDATA%\Microsoft\Windows\ConnectedSearch\History</c>.
    /// </summary>
    public static KnownFolder SearchHistory { get; } = new("History", "PERUSER", new Guid(0x0D4C3DB6, 0x03A3, 0x462F, 0xA0, 0xE6, 0x08, 0x92, 0x4C, 0x41, 0xB5, 0xD4), @"%LOCALAPPDATA%\Microsoft\Windows\ConnectedSearch\History");


    /// <summary>
    /// Represents the Search Results folder.
    /// Default path: <c>Not applicable—virtual folder</c>.
    /// </summary>
    public static KnownFolder SearchHome { get; } = new("Search Results", "VIRTUAL", new Guid(0x190337d1, 0xb8ca, 0x4121, 0xa6, 0x39, 0x6d, 0x47, 0x2d, 0x16, 0x97, 0x2a), @"Not applicable—virtual folder");


    /// <summary>
    /// Represents the Microsoft Office Outlook folder.
    /// Default path: <c>Not applicable—virtual folder</c>.
    /// </summary>
    public static KnownFolder SEARCH_MAPI { get; } = new("Microsoft Office Outlook", "VIRTUAL", new Guid(0x98ec0e18, 0x2098, 0x4d44, 0x86, 0x44, 0x66, 0x97, 0x93, 0x15, 0xa2, 0x81), @"Not applicable—virtual folder");


    /// <summary>
    /// Represents the Templates folder.
    /// Default path: <c>%LOCALAPPDATA%\Microsoft\Windows\ConnectedSearch\Templates</c>.
    /// </summary>
    public static KnownFolder SearchTemplates { get; } = new("Templates", "PERUSER", new Guid(0x7E636BFE, 0xDFA9, 0x4D5E, 0xB4, 0x56, 0xD7, 0xB3, 0x98, 0x51, 0xD8, 0xA9), @"%LOCALAPPDATA%\Microsoft\Windows\ConnectedSearch\Templates");


    /// <summary>
    /// Represents the SendTo folder.
    /// Default path: <c>%APPDATA%\Microsoft\Windows\SendTo</c>.
    /// </summary>
    public static KnownFolder SendTo { get; } = new("SendTo", "PERUSER", new Guid(0x8983036C, 0x27C0, 0x404B, 0x8F, 0x08, 0x10, 0x2D, 0x10, 0xDC, 0xFD, 0x74), @"%APPDATA%\Microsoft\Windows\SendTo");


    /// <summary>
    /// Represents the Gadgets folder.
    /// Default path: <c>%ProgramFiles%\Windows Sidebar\Gadgets</c>.
    /// </summary>
    public static KnownFolder SidebarDefaultParts { get; } = new("Gadgets", "COMMON", new Guid(0x7B396E54, 0x9EC5, 0x4300, 0xBE, 0x0A, 0x24, 0x82, 0xEB, 0xAE, 0x1A, 0x26), @"%ProgramFiles%\Windows Sidebar\Gadgets");


    /// <summary>
    /// Represents the Gadgets folder.
    /// Default path: <c>%LOCALAPPDATA%\Microsoft\Windows Sidebar\Gadgets</c>.
    /// </summary>
    public static KnownFolder SidebarParts { get; } = new("Gadgets", "PERUSER", new Guid(0xA75D362E, 0x50FC, 0x4fb7, 0xAC, 0x2C, 0xA8, 0xBE, 0xAA, 0x31, 0x44, 0x93), @"%LOCALAPPDATA%\Microsoft\Windows Sidebar\Gadgets");


    /// <summary>
    /// Represents the OneDrive folder.
    /// Default path: <c>%USERPROFILE%\OneDrive</c>.
    /// </summary>
    public static KnownFolder SkyDrive { get; } = new("OneDrive", "PERUSER", new Guid(0xA52BBA46, 0xE9E1, 0x435f, 0xB3, 0xD9, 0x28, 0xDA, 0xA6, 0x48, 0xC0, 0xF6), @"%USERPROFILE%\OneDrive");


    /// <summary>
    /// Represents the Camera Roll folder.
    /// Default path: <c>%USERPROFILE%\OneDrive\Pictures\Camera Roll</c>.
    /// </summary>
    public static KnownFolder SkyDriveCameraRoll { get; } = new("Camera Roll", "PERUSER", new Guid(0x767E6811, 0x49CB, 0x4273, 0x87, 0xC2, 0x20, 0xF3, 0x55, 0xE1, 0x08, 0x5B), @"%USERPROFILE%\OneDrive\Pictures\Camera Roll");


    /// <summary>
    /// Represents the Documents folder.
    /// Default path: <c>%USERPROFILE%\OneDrive\Documents</c>.
    /// </summary>
    public static KnownFolder SkyDriveDocuments { get; } = new("Documents", "PERUSER", new Guid(0x24D89E24, 0x2F19, 0x4534, 0x9D, 0xDE, 0x6A, 0x66, 0x71, 0xFB, 0xB8, 0xFE), @"%USERPROFILE%\OneDrive\Documents");


    /// <summary>
    /// Represents the Pictures folder.
    /// Default path: <c>%USERPROFILE%\OneDrive\Pictures</c>.
    /// </summary>
    public static KnownFolder SkyDrivePictures { get; } = new("Pictures", "PERUSER", new Guid(0x339719B5, 0x8C47, 0x4894, 0x94, 0xC2, 0xD8, 0xF7, 0x7A, 0xDD, 0x44, 0xA6), @"%USERPROFILE%\OneDrive\Pictures");


    /// <summary>
    /// Represents the Start Menu folder.
    /// Default path: <c>%APPDATA%\Microsoft\Windows\Start Menu</c>.
    /// </summary>
    public static KnownFolder StartMenu { get; } = new("Start Menu", "PERUSER", new Guid(0x625B53C3, 0xAB48, 0x4EC1, 0xBA, 0x1F, 0xA1, 0xEF, 0x41, 0x46, 0xFC, 0x19), @"%APPDATA%\Microsoft\Windows\Start Menu");


    /// <summary>
    /// Represents the Startup folder.
    /// Default path: <c>%APPDATA%\Microsoft\Windows\Start Menu\Programs\StartUp</c>.
    /// </summary>
    public static KnownFolder Startup { get; } = new("Startup", "PERUSER", new Guid(0xB97D20BB, 0xF46A, 0x4C97, 0xBA, 0x10, 0x5E, 0x36, 0x08, 0x43, 0x08, 0x54), @"%APPDATA%\Microsoft\Windows\Start Menu\Programs\StartUp");


    /// <summary>
    /// Represents the Sync Center folder.
    /// Default path: <c>Not applicable—virtual folder</c>.
    /// </summary>
    public static KnownFolder SyncManagerFolder { get; } = new("Sync Center", "VIRTUAL", new Guid(0x43668BF8, 0xC14E, 0x49B2, 0x97, 0xC9, 0x74, 0x77, 0x84, 0xD7, 0x84, 0xB7), @"Not applicable—virtual folder");


    /// <summary>
    /// Represents the Sync Results folder.
    /// Default path: <c>Not applicable—virtual folder</c>.
    /// </summary>
    public static KnownFolder SyncResultsFolder { get; } = new("Sync Results", "VIRTUAL", new Guid(0x289a9a43, 0xbe44, 0x4057, 0xa4, 0x1b, 0x58, 0x7a, 0x76, 0xd7, 0xe7, 0xf9), @"Not applicable—virtual folder");


    /// <summary>
    /// Represents the Sync Setup folder.
    /// Default path: <c>Not applicable—virtual folder</c>.
    /// </summary>
    public static KnownFolder SyncSetupFolder { get; } = new("Sync Setup", "VIRTUAL", new Guid(0x0F214138, 0xB1D3, 0x4a90, 0xBB, 0xA9, 0x27, 0xCB, 0xC0, 0xC5, 0x38, 0x9A), @"Not applicable—virtual folder");


    /// <summary>
    /// Represents the System32 folder.
    /// Default path: <c>%windir%\system32</c>.
    /// </summary>
    public static KnownFolder System { get; } = new("System32", "FIXED", new Guid(0x1AC14E77, 0x02E7, 0x4E5D, 0xB7, 0x44, 0x2E, 0xB1, 0xAE, 0x51, 0x98, 0xB7), @"%windir%\system32");


    /// <summary>
    /// Represents the System32 folder.
    /// Default path: <c>%windir%\system32</c>.
    /// </summary>
    public static KnownFolder SystemX86 { get; } = new("System32", "FIXED", new Guid(0xD65231B0, 0xB2F1, 0x4857, 0xA4, 0xCE, 0xA8, 0xE7, 0xC6, 0xEA, 0x7D, 0x27), @"%windir%\system32");


    /// <summary>
    /// Represents the Templates folder.
    /// Default path: <c>%APPDATA%\Microsoft\Windows\Templates</c>.
    /// </summary>
    public static KnownFolder Templates { get; } = new("Templates", "PERUSER", new Guid(0xA63293E8, 0x664E, 0x48DB, 0xA0, 0x79, 0xDF, 0x75, 0x9E, 0x05, 0x09, 0xF7), @"%APPDATA%\Microsoft\Windows\Templates");


    /// <summary>
    /// Represents the User Pinned folder.
    /// Default path: <c>%APPDATA%\Microsoft\Internet Explorer\Quick Launch\User Pinned</c>.
    /// </summary>
    public static KnownFolder UserPinned { get; } = new("User Pinned", "PERUSER", new Guid(0x9E3995AB, 0x1F9C, 0x4F13, 0xB8, 0x27, 0x48, 0xB2, 0x4B, 0x6C, 0x71, 0x74), @"%APPDATA%\Microsoft\Internet Explorer\Quick Launch\User Pinned");


    /// <summary>
    /// Represents the Users folder.
    /// Default path: <c>%SystemDrive%\Users</c>.
    /// </summary>
    public static KnownFolder UserProfiles { get; } = new("Users", "FIXED", new Guid(0x0762D272, 0xC50A, 0x4BB0, 0xA3, 0x82, 0x69, 0x7D, 0xCD, 0x72, 0x9B, 0x80), @"%SystemDrive%\Users");


    /// <summary>
    /// Represents the Programs folder.
    /// Default path: <c>%LOCALAPPDATA%\Programs</c>.
    /// </summary>
    public static KnownFolder UserProgramFiles { get; } = new("Programs", "PERUSER", new Guid(0x5CD7AEE2, 0x2219, 0x4A67, 0xB8, 0x5D, 0x6C, 0x9C, 0xE1, 0x56, 0x60, 0xCB), @"%LOCALAPPDATA%\Programs");


    /// <summary>
    /// Represents the Programs folder.
    /// Default path: <c>%LOCALAPPDATA%\Programs\Common</c>.
    /// </summary>
    public static KnownFolder UserProgramFilesCommon { get; } = new("Programs", "PERUSER", new Guid(0xBCBD3057, 0xCA5C, 0x4622, 0xB4, 0x2D, 0xBC, 0x56, 0xDB, 0x0A, 0xE5, 0x16), @"%LOCALAPPDATA%\Programs\Common");


    /// <summary>
    /// Represents the The user's full name (for instance, Jean Philippe Bagel) entered when the user account was created. folder.
    /// Default path: <c>Not applicable—virtual folder</c>.
    /// </summary>
    public static KnownFolder UsersFiles { get; } = new("The user's full name (for instance, Jean Philippe Bagel) entered when the user account was created.", "VIRTUAL", new Guid(0xf3ce0f7c, 0x4901, 0x4acc, 0x86, 0x48, 0xd5, 0xd4, 0x4b, 0x04, 0xef, 0x8f), @"Not applicable—virtual folder");


    /// <summary>
    /// Represents the Libraries folder.
    /// Default path: <c>Not applicable—virtual folder</c>.
    /// </summary>
    public static KnownFolder UsersLibraries { get; } = new("Libraries", "VIRTUAL", new Guid(0xA302545D, 0xDEFF, 0x464b, 0xAB, 0xE8, 0x61, 0xC8, 0x64, 0x8D, 0x93, 0x9B), @"Not applicable—virtual folder");


    /// <summary>
    /// Represents the Videos folder.
    /// Default path: <c>%USERPROFILE%\Videos</c>.
    /// </summary>
    public static KnownFolder Videos { get; } = new("Videos", "PERUSER", new Guid(0x18989B1D, 0x99B5, 0x455B, 0x84, 0x1C, 0xAB, 0x7C, 0x74, 0xE4, 0xDD, 0xFC), @"%USERPROFILE%\Videos");


    /// <summary>
    /// Represents the Videos folder.
    /// Default path: <c>%APPDATA%\Microsoft\Windows\Libraries\Videos.library-ms</c>.
    /// </summary>
    public static KnownFolder VideosLibrary { get; } = new("Videos", "PERUSER", new Guid(0x491E922F, 0x5643, 0x4AF4, 0xA7, 0xEB, 0x4E, 0x7A, 0x13, 0x8D, 0x81, 0x74), @"%APPDATA%\Microsoft\Windows\Libraries\Videos.library-ms");


    /// <summary>
    /// Represents the Windows folder.
    /// Default path: <c>%windir%</c>.
    /// </summary>
    public static KnownFolder Windows { get; } = new("Windows", "FIXED", new Guid(0xF38BF404, 0x1D43, 0x42F2, 0x93, 0x05, 0x67, 0xDE, 0x0B, 0x28, 0xFC, 0x23), @"%windir%");
}
