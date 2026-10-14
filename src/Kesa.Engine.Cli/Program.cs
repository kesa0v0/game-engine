using System;
using System.Text;

namespace Kesa.Engine.Cli
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            Console.OutputEncoding = new UTF8Encoding(false);

            if (args.Length >= 1 && args[0] == "check")
                return CheckCommand.Run(args[1..], Console.Out, Console.Error);

            Console.Error.WriteLine(CheckCommand.Usage);
            return CheckCommand.ExitUsage;
        }
    }
}
