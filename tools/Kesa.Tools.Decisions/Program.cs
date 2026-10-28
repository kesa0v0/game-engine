using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Kesa.Tools.Decisions
{
    internal static class Program
    {
        private const string Usage =
            "사용법: decisions <명령> [옵션]\n" +
            "  show <G번호>                  한 결정의 전문과 바뀐 관계\n" +
            "  list [--area 영역] [--tag 태그]... [--all]   유효한 결정의 번호·제목·요약 (--all은 바뀐 것까지)\n" +
            "  search <글> [--all]           제목·요약·전문에서 글자 검색, 번호와 요약만\n" +
            "  tags                          쓰인 태그와 개수\n" +
            "  check                         머리 규칙(상태·바뀐 관계·참조·태그) 검사, 문제가 있으면 종료 코드 1\n" +
            "옵션: --dir <결정 폴더> (기본: 위로 올라가며 docs/decisions를 찾음)";

        private static int Main(string[] args)
        {
            Console.OutputEncoding = new System.Text.UTF8Encoding(false);
            var rest = new List<string>(args);
            string? dir = TakeOption(rest, "--dir") ?? FindDirectory();
            if (dir == null || !Directory.Exists(dir))
            {
                Console.Error.WriteLine("docs/decisions를 찾지 못했다.");
                return 2;
            }
            if (rest.Count == 0)
            {
                Console.Error.WriteLine(Usage);
                return 2;
            }
            var all = Decision.LoadAll(dir);
            string command = rest[0];
            rest.RemoveAt(0);
            switch (command)
            {
                case "show": return Show(all, rest);
                case "list": return List(all, rest);
                case "search": return Search(all, rest);
                case "tags": return Tags(all);
                case "check": return Check(all);
                default: Console.Error.WriteLine(Usage); return 2;
            }
        }

        private static int Show(List<Decision> all, List<string> args)
        {
            if (args.Count != 1) { Console.Error.WriteLine("show <G번호>"); return 2; }
            string number = args[0].StartsWith("G", StringComparison.OrdinalIgnoreCase) ? args[0].Substring(1) : args[0];
            string id = "G" + number;
            var d = all.FirstOrDefault(x => x.Id == id);
            if (d == null) { Console.Error.WriteLine(id + " 없음"); return 1; }
            Console.WriteLine(d.Id + " [" + d.Area + "] " + d.Status + "  " + d.Title);
            if (d.Tags.Count > 0) Console.WriteLine("태그: " + string.Join(", ", d.Tags));
            if (d.ChangedBy.Count > 0) Console.WriteLine("바뀐 쪽: " + string.Join(", ", d.ChangedBy));
            var changes = all.Where(x => x.ChangedBy.Contains(d.Id)).Select(x => x.Id).ToList();
            if (changes.Count > 0) Console.WriteLine("이것이 바꾼 것: " + string.Join(", ", changes));
            if (d.Refs.Count > 0) Console.WriteLine("본문이 언급한 것: " + string.Join(", ", d.Refs));
            Console.WriteLine();
            Console.WriteLine(d.Body);
            return 0;
        }

        private static int List(List<Decision> all, List<string> args)
        {
            bool includeInactive = TakeFlag(args, "--all");
            string? area = TakeOption(args, "--area");
            var tags = TakeOptions(args, "--tag");
            if (args.Count > 0) { Console.Error.WriteLine("모르는 인자: " + args[0]); return 2; }
            var found = all.Where(d => (includeInactive || d.Active)
                && (area == null || d.Area.Contains(area, StringComparison.OrdinalIgnoreCase))
                && tags.All(t => d.Tags.Contains(t))).ToList();
            foreach (var d in found) Print(d);
            Console.Error.WriteLine(found.Count + "건");
            return 0;
        }

        private static int Search(List<Decision> all, List<string> args)
        {
            bool includeInactive = TakeFlag(args, "--all");
            if (args.Count == 0) { Console.Error.WriteLine("search <글>"); return 2; }
            string needle = string.Join(" ", args);
            var found = all.Where(d => (includeInactive || d.Active)
                && (d.Title.Contains(needle, StringComparison.OrdinalIgnoreCase)
                    || d.Summary.Contains(needle, StringComparison.OrdinalIgnoreCase)
                    || d.Body.Contains(needle, StringComparison.OrdinalIgnoreCase))).ToList();
            foreach (var d in found) Print(d);
            Console.Error.WriteLine(found.Count + "건");
            return 0;
        }

        private static int Tags(List<Decision> all)
        {
            foreach (var g in all.SelectMany(d => d.Tags).GroupBy(t => t).OrderByDescending(g => g.Count()))
                Console.WriteLine(g.Key + "\t" + g.Count());
            return 0;
        }

        private static int Check(List<Decision> all)
        {
            var problems = DecisionCheck.Run(all);
            foreach (var p in problems) Console.WriteLine(p);
            Console.Error.WriteLine(all.Count + "건 중 문제 " + problems.Count + "개");
            return problems.Count == 0 ? 0 : 1;
        }

        private static void Print(Decision d) =>
            Console.WriteLine(d.Id + (d.Status == "확인" ? "" : " (" + d.Status + ")") + " [" + d.Title + "] " + d.Summary);

        private static string? FindDirectory()
        {
            for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir != null; dir = dir.Parent)
            {
                string candidate = Path.Combine(dir.FullName, "docs", "decisions");
                if (Directory.Exists(candidate)) return candidate;
            }
            return null;
        }

        private static bool TakeFlag(List<string> args, string name) => args.Remove(name);

        private static string? TakeOption(List<string> args, string name)
        {
            int i = args.IndexOf(name);
            if (i < 0 || i + 1 >= args.Count) return null;
            string value = args[i + 1];
            args.RemoveRange(i, 2);
            return value;
        }

        private static List<string> TakeOptions(List<string> args, string name)
        {
            var values = new List<string>();
            string? v;
            while ((v = TakeOption(args, name)) != null) values.Add(v);
            return values;
        }
    }
}
