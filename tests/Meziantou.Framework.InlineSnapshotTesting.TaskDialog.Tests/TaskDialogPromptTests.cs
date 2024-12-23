using System.Windows.Automation;
using Meziantou.Framework.InlineSnapshotTesting.SnapshotUpdateStrategies;
using TestUtilities;
using Xunit;

namespace Meziantou.Framework.InlineSnapshotTesting.TaskDialog.Tests;

public sealed class TaskDialogPromptTests
{
    private PromptResult Invoke(PromptContext context, int buttonIndex, bool applyToAllFiles)
    {
        // The project is multi-targeted, so multiple process can run in parallel
        using var mutex = new Mutex(initiallyOwned: false, "MeziantouFrameworkTaskDialogPromptTests");
        mutex.WaitOne();
        try
        {
            var prompt = new TaskDialogPrompt()
            {
                MustRegisterUriScheme = false,
                MustStartNotificationTray = false,
                MustShowDialog = true,
            };

            Automation.AddAutomationEventHandler(
                eventId: WindowPattern.WindowOpenedEvent,
                element: AutomationElement.RootElement,
                scope: TreeScope.Children,
                eventHandler: OnWindowOpened);

            try
            {
                return prompt.Ask(context);
            }
            finally
            {
                Automation.RemoveAutomationEventHandler(WindowPattern.WindowOpenedEvent, AutomationElement.RootElement, OnWindowOpened);
            }
        }
        finally
        {
            mutex.ReleaseMutex();
        }

        void OnWindowOpened(object sender, AutomationEventArgs automationEventArgs)
        {
            try
            {
                var element = sender as AutomationElement;
                if (element != null)
                {
                    if (element.Current.Name is "InlineSnapshot Configuration")
                    {
                        var first = TreeWalker.ControlViewWalker.GetFirstChild(element);

                        if (applyToAllFiles)
                        {
                            var checkbox = element.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.CheckBox));
                            if (checkbox.TryGetCurrentPattern(TogglePattern.Pattern, out var checkboxPattern))
                                ((TogglePattern)checkboxPattern).Toggle();
                        }

                        var btns = element.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
                        var btn = btns[buttonIndex];

                        if (btn.TryGetCurrentPattern(InvokePattern.Pattern, out var btnPattern))
                            ((InvokePattern)btnPattern).Invoke();
                    }
                }
            }
            catch (ElementNotAvailableException)
            {
            }
        }
    }

    [Theory, RunIf(FactOperatingSystem.Windows)]
    [InlineData(0, false, PromptConfigurationMode.Disallow, PromptConfigurationScope.CurrentFile)]
    [InlineData(1, false, PromptConfigurationMode.MergeTool, PromptConfigurationScope.CurrentFile)]
    [InlineData(2, false, PromptConfigurationMode.Overwrite, PromptConfigurationScope.CurrentFile)]
    [InlineData(3, false, PromptConfigurationMode.OverwriteWithoutFailure, PromptConfigurationScope.CurrentFile)]
    [InlineData(0, true, PromptConfigurationMode.Disallow, PromptConfigurationScope.ParentProcess)]
    internal void Ask(int buttonIndex, bool applyToAllFiles, PromptConfigurationMode expectedMode, PromptConfigurationScope expectedScope)
    {
        var result = Invoke(new PromptContext("path.cs", "dummy test", ParentProcessInfo: null), buttonIndex, applyToAllFiles);

        Assert.Equal(expectedMode, result.Mode);
        Assert.Equal(expectedScope, result.Scope);
        Assert.Equal(TimeSpan.FromHours(1), result.RememberPeriod);
    }

    [Fact, RunIf(FactOperatingSystem.Windows)]
    internal void Ask_Cancel()
    {
        var result = Invoke(new PromptContext("path.cs", "dummy test", ParentProcessInfo: null), buttonIndex: 4, applyToAllFiles: false);

        Assert.Equal(PromptConfigurationMode.Disallow, result.Mode);
        Assert.Equal(PromptConfigurationScope.CurrentSnapshot, result.Scope);
        Assert.True(result.RememberPeriod <= TimeSpan.Zero);
    }
}
