namespace Meziantou.Framework.Diagnostics.ContextSnapshot;

/// <summary>Represents a snapshot of special folder paths at a specific point in time.</summary>
public sealed class SpecialFolderSnapshot
{
    /// <summary>Gets the path to the Admin Tools folder.</summary>
    /// <summary>Gets the path to the Admin Tools folder.</summary>
    public string AdminTools { get; } = Environment.GetFolderPath(Environment.SpecialFolder.AdminTools);
    /// <summary>Gets the path to the Application Data folder.</summary>
    public string ApplicationData { get; } = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    /// <summary>Gets the path to the CD Burning folder.</summary>
    public string CDBurning { get; } = Environment.GetFolderPath(Environment.SpecialFolder.CDBurning);
    /// <summary>Gets the path to the Common Admin Tools folder.</summary>
    public string CommonAdminTools { get; } = Environment.GetFolderPath(Environment.SpecialFolder.CommonAdminTools);
    /// <summary>Gets the path to the Common Application Data folder.</summary>
    public string CommonApplicationData { get; } = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
    /// <summary>Gets the path to the Common Desktop folder.</summary>
    public string CommonDesktopDirectory { get; } = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
    /// <summary>Gets the path to the Common Documents folder.</summary>
    public string CommonDocuments { get; } = Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments);
    /// <summary>Gets the path to the Common Music folder.</summary>
    public string CommonMusic { get; } = Environment.GetFolderPath(Environment.SpecialFolder.CommonMusic);
    /// <summary>Gets the path to the Common OEM Links folder.</summary>
    public string CommonOemLinks { get; } = Environment.GetFolderPath(Environment.SpecialFolder.CommonOemLinks);
    /// <summary>Gets the path to the Common Pictures folder.</summary>
    public string CommonPictures { get; } = Environment.GetFolderPath(Environment.SpecialFolder.CommonPictures);
    /// <summary>Gets the path to the Common Program Files folder.</summary>
    public string CommonProgramFiles { get; } = Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles);
    /// <summary>Gets the path to the Common Program Files (x86) folder.</summary>
    public string CommonProgramFilesX86 { get; } = Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFilesX86);
    /// <summary>Gets the path to the Common Programs folder.</summary>
    public string CommonPrograms { get; } = Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms);
    /// <summary>Gets the path to the Common Start Menu folder.</summary>
    public string CommonStartMenu { get; } = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu);
    /// <summary>Gets the path to the Common Startup folder.</summary>
    public string CommonStartup { get; } = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup);
    /// <summary>Gets the path to the Common Templates folder.</summary>
    public string CommonTemplates { get; } = Environment.GetFolderPath(Environment.SpecialFolder.CommonTemplates);
    /// <summary>Gets the path to the Common Videos folder.</summary>
    public string CommonVideos { get; } = Environment.GetFolderPath(Environment.SpecialFolder.CommonVideos);
    /// <summary>Gets the path to the Cookies folder.</summary>
    public string Cookies { get; } = Environment.GetFolderPath(Environment.SpecialFolder.Cookies);
    /// <summary>Gets the path to the Desktop folder.</summary>
    public string Desktop { get; } = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
    /// <summary>Gets the path to the Desktop Directory folder.</summary>
    public string DesktopDirectory { get; } = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
    /// <summary>Gets the path to the Favorites folder.</summary>
    public string Favorites { get; } = Environment.GetFolderPath(Environment.SpecialFolder.Favorites);
    /// <summary>Gets the path to the Fonts folder.</summary>
    public string Fonts { get; } = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
    /// <summary>Gets the path to the History folder.</summary>
    public string History { get; } = Environment.GetFolderPath(Environment.SpecialFolder.History);
    /// <summary>Gets the path to the Internet Cache folder.</summary>
    public string InternetCache { get; } = Environment.GetFolderPath(Environment.SpecialFolder.InternetCache);
    /// <summary>Gets the path to the Local Application Data folder.</summary>
    public string LocalApplicationData { get; } = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    /// <summary>Gets the path to the Localized Resources folder.</summary>
    public string LocalizedResources { get; } = Environment.GetFolderPath(Environment.SpecialFolder.LocalizedResources);
    /// <summary>Gets the path to the My Computer folder.</summary>
    public string MyComputer { get; } = Environment.GetFolderPath(Environment.SpecialFolder.MyComputer);
    /// <summary>Gets the path to the My Documents folder.</summary>
    public string MyDocuments { get; } = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    /// <summary>Gets the path to the My Music folder.</summary>
    public string MyMusic { get; } = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
    /// <summary>Gets the path to the My Pictures folder.</summary>
    public string MyPictures { get; } = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
    /// <summary>Gets the path to the My Videos folder.</summary>
    public string MyVideos { get; } = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
    /// <summary>Gets the path to the Network Shortcuts folder.</summary>
    public string NetworkShortcuts { get; } = Environment.GetFolderPath(Environment.SpecialFolder.NetworkShortcuts);
    /// <summary>Gets the path to the Personal folder.</summary>
    public string Personal { get; } = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
    /// <summary>Gets the path to the Printer Shortcuts folder.</summary>
    public string PrinterShortcuts { get; } = Environment.GetFolderPath(Environment.SpecialFolder.PrinterShortcuts);
    /// <summary>Gets the path to the Program Files folder.</summary>
    public string ProgramFiles { get; } = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
    /// <summary>Gets the path to the Program Files (x86) folder.</summary>
    public string ProgramFilesX86 { get; } = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
    /// <summary>Gets the path to the Programs folder.</summary>
    public string Programs { get; } = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
    /// <summary>Gets the path to the Recent folder.</summary>
    public string Recent { get; } = Environment.GetFolderPath(Environment.SpecialFolder.Recent);
    /// <summary>Gets the path to the Resources folder.</summary>
    public string Resources { get; } = Environment.GetFolderPath(Environment.SpecialFolder.Resources);
    /// <summary>Gets the path to the SendTo folder.</summary>
    public string SendTo { get; } = Environment.GetFolderPath(Environment.SpecialFolder.SendTo);
    /// <summary>Gets the path to the Start Menu folder.</summary>
    public string StartMenu { get; } = Environment.GetFolderPath(Environment.SpecialFolder.StartMenu);
    /// <summary>Gets the path to the Startup folder.</summary>
    public string Startup { get; } = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
    /// <summary>Gets the path to the System folder.</summary>
    public string System { get; } = Environment.GetFolderPath(Environment.SpecialFolder.System);
    /// <summary>Gets the path to the System (x86) folder.</summary>
    public string SystemX86 { get; } = Environment.GetFolderPath(Environment.SpecialFolder.SystemX86);
    /// <summary>Gets the path to the Templates folder.</summary>
    public string Templates { get; } = Environment.GetFolderPath(Environment.SpecialFolder.Templates);
    /// <summary>Gets the path to the User Profile folder.</summary>
    public string UserProfile { get; } = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    /// <summary>Gets the path to the Windows folder.</summary>
    public string Windows { get; } = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
}
