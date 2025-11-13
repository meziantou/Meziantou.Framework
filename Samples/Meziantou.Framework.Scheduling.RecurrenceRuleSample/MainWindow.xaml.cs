using System.Windows;
using System.Windows.Controls;

namespace Meziantou.Framework.Scheduling.RecurrenceRuleSample;

/// <summary>Interaction logic for MainWindow.xaml</summary>
public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        TbxRecurrenceRule.Text = "FREQ=DAILY;COUNT=10";
    }

    private void TextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateNextOccurrences();
    }

    [SuppressMessage("ApiDesign", "RS0030:Do not used banned APIs", Justification = "Use for UI")]
    private void UpdateNextOccurrences()
    {
        if (RecurrenceRule.TryParse(TbxRecurrenceRule.Text, out var rule, out var error))
        {
            var occurrences = rule.GetNextOccurrences(DateTime.Now).Select(date => date.ToString("F", CultureInfo.CurrentCulture)).Take(50).ToList();
            NextOccurences.ItemsSource = occurrences;
        }
        else
        {
            NextOccurences.ItemsSource = new[] { "Expression is not valid", error };
        }
    }
}
