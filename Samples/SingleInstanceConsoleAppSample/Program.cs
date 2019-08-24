using System;
using Meziantou.Framework;

namespace SingleInstanceConsoleAppSample
{
    internal static class Program
    {
        private static void Main(string[] args)
        {
            Console.WriteLine("Hello World!");
            using var singleInstance = new SingleInstance(new Guid("e5d38b35-b275-47f4-8aa6-bcee6361aae9"));
            if (!singleInstance.StartApplication())
            {
                Console.WriteLine("Sending args to first app");
                singleInstance.NotifyFirstInstance(args);
                return;
            }

            singleInstance.NewInstance += SingleInstance_NewInstance;

            Console.WriteLine("Waiting for other instances");
            Console.ReadLine();
        }

        private static void SingleInstance_NewInstance(object sender, SingleInstanceEventArgs e)
        {
            Console.WriteLine("New instance " + e.ProcessId);
            foreach (var arg in e.Arguments)
            {
                Console.WriteLine(arg);
            }
        }
    }
}
