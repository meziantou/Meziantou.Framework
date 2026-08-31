namespace Meziantou.Framework;

/// <summary>Provides methods for creating interactive yes/no prompts in console applications.</summary>
public static class Prompt
{
    /// <summary>Prompts the user with a yes/no question using standard Y/N labels.</summary>
    /// <param name="question">The question to display to the user.</param>
    /// <param name="defaultValue">The default value to use if the user presses Enter without typing a response, or if the standard input is closed. If <see langword="null"/>, the user must provide an explicit answer.</param>
    /// <returns><see langword="true"/> if the user answered yes; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="EndOfStreamException">The standard input is closed and <paramref name="defaultValue"/> is <see langword="null"/>.</exception>
    public static bool YesNo(string question, bool? defaultValue)
    {
        if (defaultValue.HasValue)
        {
            if (defaultValue.Value)
            {
                return YesNo(question, "Y", "n", defaultValue: true);
            }
            else
            {
                return YesNo(question, "y", "N", defaultValue: false);
            }
        }
        else
        {
            return YesNo(question, "y", "n", defaultValue: null);
        }
    }

    /// <summary>Prompts the user with a yes/no question using custom labels. The prompt loops until the user provides a valid response.</summary>
    /// <param name="question">The question to display to the user.</param>
    /// <param name="yesValue">The text representing a yes response (case-insensitive).</param>
    /// <param name="noValue">The text representing a no response (case-insensitive).</param>
    /// <param name="defaultValue">The default value to use if the user presses Enter without typing a response, or if the standard input is closed. If <see langword="null"/>, the user must provide an explicit answer.</param>
    /// <returns><see langword="true"/> if the user answered yes; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="EndOfStreamException">The standard input is closed and <paramref name="defaultValue"/> is <see langword="null"/>.</exception>
    public static bool YesNo(string question, string yesValue, string noValue, bool? defaultValue)
    {
        while (true)
        {
            Console.Write($"{question} [{yesValue}/{noValue}] ");
            var result = Console.ReadLine();
            if (result is null)
            {
                // The input is closed, so asking again would loop forever without ever reading a different answer.
                if (defaultValue.HasValue)
                    return defaultValue.Value;

                throw new EndOfStreamException("Cannot read the answer because the standard input is closed and no default value is provided");
            }

            if (result.Length == 0)
            {
                if (defaultValue.HasValue)
                    return defaultValue.Value;

                continue;
            }

            if (string.Equals(result, yesValue, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(result, noValue, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }
    }
}
