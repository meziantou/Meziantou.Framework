using System;

namespace ArgumentsPrinter;

internal static class Program
{
    private static void Main(string[] args)
    {
        foreach (var argument in args)
        {
            Console.WriteLine(argument);
        }
    }
}
