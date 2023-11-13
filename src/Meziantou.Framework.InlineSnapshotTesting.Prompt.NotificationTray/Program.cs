using Meziantou.Framework.InlineSnapshotTesting.SnapshotUpdateStrategies;

namespace Meziantou.Framework.InlineSnapshotTesting.Prompt.NotificationTray;

internal static class Program
{
    [STAThread]
    public static void Main()
    {
        const string AppId = "Local\\6495bb03-4c5c-4695-bb73-310b06982b5c";
        using var mutex = new Mutex(initiallyOwned: false, AppId);
        if (!mutex.WaitOne(0))
            return;

        Application.EnableVisualStyles();
        using var appContext = new MyCustomApplicationContext();
        Application.Run(appContext);
    }

    private sealed class MyCustomApplicationContext : ApplicationContext
    {
        private readonly NotifyIcon _trayIcon;

        public MyCustomApplicationContext()
        {
            using var stream = typeof(Program).Assembly.GetManifestResourceStream("Meziantou.Framework.InlineSnapshotTesting.Prompt.NotificationTray.Assets.TrayIcon.ico")!;

            var defaultMode = GetConfigurationMode();

            var modeMenu = new ToolStripMenuItem("Update mode", image: null)
            {
                DropDownItems =
                {
                    new ToolStripRadioButtonMenuItem("Ask", UpdateModeClick) { Tag = null },
                    new ToolStripRadioButtonMenuItem("Do not update snapshots", UpdateModeClick) { Tag = PromptConfigurationMode.Disallow },
                    new ToolStripRadioButtonMenuItem("Open merge tool", UpdateModeClick) { Tag = PromptConfigurationMode.MergeTool },
                    new ToolStripRadioButtonMenuItem("Update snapshots", UpdateModeClick) { Tag = PromptConfigurationMode.Overwrite },
                    new ToolStripRadioButtonMenuItem("Update snapshots without failing", UpdateModeClick) { Tag = PromptConfigurationMode.OverwriteWithoutFailure },
                },
            };

            foreach (ToolStripMenuItem item in modeMenu.DropDownItems)
            {
                if ((PromptConfigurationMode?)item.Tag == defaultMode)
                {
                    item.Checked = true;
                }
            }

            _trayIcon = new NotifyIcon()
            {
                Text = "InlineSnapshotTesting configuration",
                Icon = new Icon(stream),
                ContextMenuStrip = new ContextMenuStrip
                {
                    Items =
                    {
                        new ToolStripMenuItem("InlineSnapshotTesting", image: null) { Enabled = false },
                        new ToolStripSeparator(),
                        modeMenu,
                        new ToolStripMenuItem("Reset settings", image: null, ResetSettingsClick),
                        new ToolStripMenuItem("Exit", image: null, Exit),
                    },
                },
                Visible = true,
            };
        }

        private static PromptConfigurationMode? GetConfigurationMode()
        {
            using var configuration = PromptConfigurationFile.LoadFromDefaultPath();
            return configuration.DefaultMode;
        }

        private void UpdateModeClick(object? sender, EventArgs e)
        {
            using var configuration = PromptConfigurationFile.LoadFromDefaultPath();
            configuration.DefaultMode = (PromptConfigurationMode?)((ToolStripRadioButtonMenuItem)sender!).Tag;
            configuration.Save();
        }

        private void ResetSettingsClick(object? sender, EventArgs e)
        {
            for (var i = 0; i < 10; i++)
            {
                try
                {
                    File.Delete(PromptConfigurationFile.DefaultFilePath);
                    return;
                }
                catch (FileNotFoundException)
                {
                    return;
                }
                catch (DirectoryNotFoundException)
                {
                    return;
                }
                catch
                {
                    Thread.Sleep(10);
                }
            }

            MessageBox.Show("Cannot delete the configuration file. Close all prompt and retry.");
        }

        private void Exit(object? sender, EventArgs e)
        {
            // Hide tray icon, otherwise it will remain shown until user mouses over it
            _trayIcon.Visible = false;
            Application.Exit();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _trayIcon.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    private sealed class ToolStripRadioButtonMenuItem : ToolStripMenuItem
    {
        public ToolStripRadioButtonMenuItem(string text, EventHandler onClick)
            : base(text, image: null, onClick)
        {
            CheckOnClick = true;
        }

        protected override void OnCheckedChanged(EventArgs e)
        {
            base.OnCheckedChanged(e);

            // If this item is no longer in the checked state or if its
            // parent has not yet been initialized, do nothing.
            if (!Checked || Parent is null)
                return;

            // Clear the checked state for all siblings.
            foreach (ToolStripItem item in Parent.Items)
            {
                if (item is ToolStripRadioButtonMenuItem radioItem && radioItem != this && radioItem.Checked)
                {
                    radioItem.Checked = false;

                    // Only one item can be selected at a time, so there is no need to continue.
                    return;
                }
            }
        }
    }
}
