using System;
using System.Text;

namespace Kesa.Engine.Cli
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            Console.OutputEncoding = new UTF8Encoding(false);
            return Commands.Run(args, Console.Out, Console.Error);
        }
    }
}
