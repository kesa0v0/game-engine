using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Kesa.Engine.Loading;
using Kesa.Engine.Runtime;

namespace Kesa.Engine.ConsoleHost
{
    /// <summary>
    /// 콘솔의 UI 파일 (spec-gaps G250): 모드 폴더의 <c>ui/console.txt</c>, 한 줄에 <c>모드.함수</c> 하나. <c>#</c> 뒤와 빈 줄은 무시하고
    /// 여러 모드에 있으면 로드 순서대로 모은다. 엔진은 이 파일을 모른다(G249).
    /// </summary>
    public static class ConsoleUi
    {
        public const string FileName = "ui/console.txt";

        public sealed class Panel
        {
            public Panel(string function, string place)
            {
                Function = function;
                Place = place;
            }

            /// <summary><c>모드.함수</c>. 자리가 앉은 엔티티 하나를 받는다.</summary>
            public string Function { get; }

            /// <summary>오류에 붙일 <c>모드폴더/ui/console.txt:줄</c>.</summary>
            public string Place { get; }
        }

        public static List<Panel> Load(string root, ContentLoadResult content)
        {
            var panels = new List<Panel>();
            foreach (var mod in content.Mods)
            {
                string path = Path.Combine(root, mod.Directory, "ui", "console.txt");
                if (!File.Exists(path)) continue;
                string[] lines = File.ReadAllLines(path);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    int hash = line.IndexOf('#');
                    if (hash >= 0) line = line.Substring(0, hash);
                    line = line.Trim();
                    if (line.Length == 0) continue;
                    panels.Add(new Panel(line, mod.Directory + "/" + FileName + ":" + (i + 1).ToString(CultureInfo.InvariantCulture)));
                }
            }
            return panels;
        }

        /// <summary>시작할 때 줄마다 부를 수 있는지 본다 (G247 <c>CheckCall</c>). 틀린 줄마다 <c>곳: 이유</c> 한 줄.</summary>
        public static List<string> Check(World world, IEnumerable<Panel> panels)
        {
            var errors = new List<string>();
            foreach (var panel in panels)
            {
                string? why = world.CheckCall(panel.Function, 1);
                if (why != null) errors.Add(panel.Place + ": " + why);
            }
            return errors;
        }
    }
}
