using System;
using System.Text;

namespace Kesa.Engine.ConsoleHost
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            Console.OutputEncoding = new UTF8Encoding(false);
            return Play.Run(args, Console.In, Console.Out, Console.Error);
        }
    }
}
