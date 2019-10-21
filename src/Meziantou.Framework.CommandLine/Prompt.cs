using System;

namespace Meziantou.Framework
{
    public static class Prompt
    {
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

        public static bool YesNo(string question, string yesValue, string noValue, bool? defaultValue)
        {
            while (true)
            {
                Console.Write($"{question} [{yesValue}/{noValue}] ");
                var result = Console.ReadLine();
                if (string.IsNullOrEmpty(result))
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
}
