using System.Windows.Automation;
using FluentAssertions;
using Meziantou.Framework.InlineSnapshotTesting.SnapshotUpdateStrategies;
using TestUtilities;
using Xunit;

namespace Meziantou.Framework.InlineSnapshotTesting.TaskDialog.Tests.SnapshotUpdateStrategies;

public sealed class TaskDialogPromptTests
{
    private PromptResult Invoke(PromptContext context, int buttonIndex, bool applyToAllFiles)
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
                            {
                                ((TogglePattern)checkboxPattern).Toggle();
                            }
                        }

                        var btns = element.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
                        var btn = btns[buttonIndex];

                        if (btn.TryGetCurrentPattern(InvokePattern.Pattern, out var btnPattern))
                        {
                            ((InvokePattern)btnPattern).Invoke();
                        }
                    }
                }
            }
            catch (ElementNotAvailableException)
            {
            }
        }
    }

    [RunIfTheory(FactOperatingSystem.Windows)]
    [InlineData(0, false, PromptConfigurationMode.Disallow, PromptConfigurationScope.CurrentFile)]
    [InlineData(1, false, PromptConfigurationMode.MergeTool, PromptConfigurationScope.CurrentFile)]
    [InlineData(2, false, PromptConfigurationMode.Overwrite, PromptConfigurationScope.CurrentFile)]
    [InlineData(3, false, PromptConfigurationMode.OverwriteWithoutFailure, PromptConfigurationScope.CurrentFile)]
    [InlineData(0, true, PromptConfigurationMode.Disallow, PromptConfigurationScope.ParentProcess)]
    internal void Ask(int buttonIndex, bool applyToAllFiles, PromptConfigurationMode expectedMode, PromptConfigurationScope expectedScope)
    {
        var result = Invoke(new PromptContext("path.cs", "dummy test", ParentProcessInfo: null), buttonIndex, applyToAllFiles);

        result.Mode.Should().Be(expectedMode);
        result.Scope.Should().Be(expectedScope);
        result.RememberPeriod.Should().Be(TimeSpan.FromHours(1));
    }

    [RunIfFact(FactOperatingSystem.Windows)]
    internal void Ask_Cancel()
    {
        var result = Invoke(new PromptContext("path.cs", "dummy test", ParentProcessInfo: null), buttonIndex: 4, applyToAllFiles: false);

        result.Mode.Should().Be(PromptConfigurationMode.Disallow);
        result.Scope.Should().Be(PromptConfigurationScope.CurrentSnapshot);
        result.RememberPeriod.Should().BeLessThanOrEqualTo(TimeSpan.Zero);
    }
}
