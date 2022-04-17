using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace Meziantou.Framework.Scheduling.RecurrenceRuleSample;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        TbxRecurrenceRule.Text = "FREQ=DAILY;COUNT=10";
    }

    private void TextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateNextOccurences();
    }

    [SuppressMessage("ApiDesign", "RS0030:Do not used banned APIs", Justification = "Use for UI")]
    private void UpdateNextOccurences()
    {
        if (RecurrenceRule.TryParse(TbxRecurrenceRule.Text, out var rule, out var error))
        {
            var occurences = rule.GetNextOccurrences(DateTime.Now).Select(date => date.ToString("F", CultureInfo.CurrentCulture)).Take(50).ToList();
            NextOccurences.ItemsSource = occurences;
        }
        else
        {
            NextOccurences.ItemsSource = new[] { "Expression is not valid", error };
        }
    }
}
