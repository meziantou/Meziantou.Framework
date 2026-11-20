using Meziantou.Xunit;
using Windows.Win32.UI.Shell;

namespace Meziantou.Framework.Win32.Tests;

public class OpenFolderDialogTests
{
    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void CreateOptions_PreservesCurrentDirectoryByDefault()
    {
        var options = OpenFolderDialog.CreateOptions(changeCurrentDirectory: false);

        Assert.Equal(FILEOPENDIALOGOPTIONS.FOS_FORCEFILESYSTEM | FILEOPENDIALOGOPTIONS.FOS_PICKFOLDERS | FILEOPENDIALOGOPTIONS.FOS_NOCHANGEDIR, options);
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void CreateOptions_AllowsChangingCurrentDirectory()
    {
        var options = OpenFolderDialog.CreateOptions(changeCurrentDirectory: true);

        Assert.Equal(FILEOPENDIALOGOPTIONS.FOS_FORCEFILESYSTEM | FILEOPENDIALOGOPTIONS.FOS_PICKFOLDERS, options);
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void TryCreateShellItem_ResolvesAnExistingDirectory()
    {
        var directory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        var item = unsafe(OpenFolderDialog.TryCreateShellItem(directory));

        Assert.NotEqual(0, (nint)item);
        try
        {
            Assert.Equal(directory, unsafe(OpenFolderDialog.GetFileSystemPath(item)), ignoreCase: true);
        }
        finally
        {
            unsafe
            {
                item->Release();
            }
        }
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void TryCreateShellItem_ResolvesADirectoryWrittenWithForwardSlashes()
    {
        // The shell parser rejects "C:/Windows" with E_INVALIDARG even though Directory.Exists
        // returns true for it, so the value has to be normalized before it is resolved.
        var directory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var forwardSlashes = directory.Replace('\\', '/');

        var item = unsafe(OpenFolderDialog.TryCreateShellItem(forwardSlashes));

        Assert.NotEqual(0, (nint)item);
        try
        {
            Assert.Equal(directory, unsafe(OpenFolderDialog.GetFileSystemPath(item)), ignoreCase: true);
        }
        finally
        {
            unsafe
            {
                item->Release();
            }
        }
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void TryCreateShellItem_ReturnsNullForADriveThatIsNotMounted()
    {
        var mounted = DriveInfo.GetDrives().Select(drive => char.ToUpperInvariant(drive.Name[0])).ToHashSet();
        var letter = "ZYXWVU".FirstOrDefault(candidate => !mounted.Contains(candidate));
        global::Xunit.Assert.SkipWhen(letter is '\0', "Every candidate drive letter is in use on this machine");

        Assert.Equal(0, (nint)unsafe(OpenFolderDialog.TryCreateShellItem($@"{letter}:\some\folder")));
    }

    [Theory, RunIf(TestOperatingSystems.Windows)]
    [InlineData(@"C:\this-directory-does-not-exist")]
    [InlineData(@"C:\this-directory\does-not\exist")]
    [InlineData(@"C:\invalid|character")]
    [InlineData("   ")]
    public void TryCreateShellItem_ReturnsNullForAnUnresolvableValue(string path)
    {
        Assert.Equal(0, (nint)unsafe(OpenFolderDialog.TryCreateShellItem(path)));
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void TryCreateShellItem_ResolvesAShellLocation()
    {
        // "This PC" is not a file system path, so it only resolves because the value goes through
        // the shell namespace parser rather than a path parser.
        var item = unsafe(OpenFolderDialog.TryCreateShellItem("::{20D04FE0-3AEA-1069-A2D8-08002B30309D}"));

        Assert.NotEqual(0, (nint)item);
        unsafe
        {
            item->Release();
        }
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void SelectedPath_IsNullBeforeTheDialogHasBeenShown()
    {
        var dialog = new OpenFolderDialog { InitialDirectory = @"C:\Windows" };

        Assert.Null(dialog.SelectedPath);
        Assert.Equal(0, dialog.LastHResult);
    }

    [Fact]
    public void SelectedPath_IsNotPubliclySettable()
    {
        // ShowDialog resets SelectedPath on every call, so a caller must not be able to seed it
        // and read back a value the user never chose.
        var setter = typeof(OpenFolderDialog).GetProperty(nameof(OpenFolderDialog.SelectedPath))!.SetMethod;

        Assert.NotNull(setter);
        Assert.False(setter.IsPublic);
    }
}
