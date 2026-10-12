using System;

namespace Kesa.Engine.Cli
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            if (args.Length >= 1 && args[0] == "check")
            {
                Console.Error.WriteLine("check: not implemented yet");
                return 2;
            }

            Console.Error.WriteLine("usage: engine check <mods-dir> [--json]");
            return 64;
        }
    }
}
