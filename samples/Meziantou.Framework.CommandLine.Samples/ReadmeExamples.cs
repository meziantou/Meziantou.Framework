namespace Meziantou.Framework.CommandLine.Samples;

internal static class ReadmeExamples
{
    public static void Prompts()
    {
        #region Prompt_YesNo
        // Simple yes/no prompt with default value
        var proceed = Prompt.YesNo("Do you want to continue?", defaultValue: true);
        // Displays: Do you want to continue? [Y/n]
        // User can press Enter to use default (true)

        // Without default value
        var confirm = Prompt.YesNo("Are you sure?", defaultValue: null);
        // Displays: Are you sure? [y/n]
        // User must enter y or n

        // Custom yes/no labels
        var delete = Prompt.YesNo("Delete file?", "Yes", "No", defaultValue: false);
        // Displays: Delete file? [y/N]

        // Prompts are case-insensitive and loop until valid input
        var result = Prompt.YesNo("Enable feature?", defaultValue: true);
        if (result)
        {
            Console.WriteLine("Feature enabled!");
        }
        #endregion

        Console.WriteLine($"{proceed} {confirm} {delete}");
    }
}
