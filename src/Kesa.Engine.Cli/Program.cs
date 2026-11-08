using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text;

namespace Kesa.Engine.Cli
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            Console.OutputEncoding = new UTF8Encoding(false);
            // `engine new`의 copy가 게임에 넣은 CLI(Tools~/cli/)는 커널 DLL을 패키지의 Plugins/에서 읽는다
            string plugins = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "Plugins"));
            if (Directory.Exists(plugins))
                AssemblyLoadContext.Default.Resolving += (context, name) =>
                {
                    string file = Path.Combine(plugins, name.Name + ".dll");
                    return File.Exists(file) ? context.LoadFromAssemblyPath(file) : null;
                };
            return Run(args);
        }

        // 커널 타입을 쓰는 코드는 위의 Resolving을 건 뒤에 컴파일되어야 한다
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int Run(string[] args) => Commands.Run(args, Console.Out, Console.Error);
    }
}
