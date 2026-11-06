using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Loading;
using Kesa.Engine.Runtime;
using Kesa.Engine.Ui;

namespace Kesa.Engine.Cli
{
    /// <summary>
    /// <c>engine check</c>의 화면 파일 검사 (G481): <c>.uxml</c>과 <c>.uss</c>를 읽고, 올리기 전 검사(<see cref="UiChecker"/>)를 돌린다.
    /// 모드가 꺼졌으면 없는 이름이 잘못 걸리므로 함수·커맨드 이름은 보지 않는다.
    /// </summary>
    internal static class UiCheck
    {
        public static void Run(ContentLoadResult content, DiagnosticBag bag)
        {
            var folders = content.Mods.Select(m => Path.Combine(content.ModsRoot, m.Directory)).ToList();
            string? Text(string path)
            {
                string? full = LookCheck.Find(path, folders);
                return full == null ? null : File.ReadAllText(full);
            }

            var problems = new List<UiProblem>();
            var documents = new SortedDictionary<string, UiDocument>(StringComparer.Ordinal);
            foreach (var path in Paths(folders))
            {
                string text = Text(path)!;
                if (path.EndsWith(".uss", StringComparison.OrdinalIgnoreCase))
                {
                    problems.AddRange(UssReader.Read(path, text).Problems);
                    continue;
                }
                var read = UxmlReader.Read(path, text);
                problems.AddRange(read.Problems);
                if (read.Succeeded) documents.Add(path, read.Document!);
            }

            var checker = Checker(content, Text);
            foreach (var document in documents.Values) checker.CheckStyles(document, problems);
            foreach (var document in Roots(documents))
                checker.CheckAsync(document, problems).GetAwaiter().GetResult(); // 부르는 쪽이 모두 동기라 바로 끝난다

            var reported = new HashSet<string>(StringComparer.Ordinal);
            foreach (var problem in problems)
            {
                if (!reported.Add(problem.ToString())) continue;
                var written = LookCheck.SpanOf(problem.Place);
                string? full = LookCheck.Find(written.File, folders);
                string file = full == null ? written.File : Path.GetRelativePath(content.ModsRoot, full).Replace('\\', '/');
                string? mod = content.Mods.FirstOrDefault(m => file.StartsWith(m.Directory + "/", StringComparison.Ordinal))?.Id;
                bag.Error(Codes.UiFileProblem, problem.Message, new SourceSpan(file, written.Line, written.Col), mod);
            }
        }

        private static IEnumerable<string> Paths(IReadOnlyList<string> folders)
        {
            var paths = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var folder in folders)
                foreach (var file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
                    if (file.EndsWith(".uxml", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".uss", StringComparison.OrdinalIgnoreCase))
                        paths.Add(Path.GetRelativePath(folder, file).Replace('\\', '/'));
            return paths;
        }

        private static UiChecker Checker(ContentLoadResult content, Func<string, string?> files)
        {
            if (content.Disabled.Count > 0)
                return new UiChecker((_, _) => new ValueTask<string?>((string?)null), null, files);
            var world = new World(content.Frozen, content.Schema, content.Scopes, content.Commands); // 이름만 찾고 돌리지 않는다
            var handlers = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (var command in content.Commands) handlers[command.Key] = command.Value.Parameters.Count;
            foreach (var action in content.UiActions) handlers[action.Key] = action.Value.Parameters.Count;
            return new UiChecker((f, n) => new ValueTask<string?>(world.CheckCall(f, n, withUi: true)), handlers, files);
        }

        // 끼워지는 파일은 끼우는 쪽에서 본다: 혼자 보면 "창 안에서만 되는 window-drag"가 잘못 걸린다. 서로 끼우는 파일은 남으므로 뒤에 본다
        private static IEnumerable<UiDocument> Roots(IReadOnlyDictionary<string, UiDocument> documents)
        {
            var included = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var document in documents.Values) Includes(document.Root, included);
            var reached = new SortedSet<string>(StringComparer.Ordinal);
            void Reach(string name)
            {
                if (!reached.Add(name) || !documents.TryGetValue(name, out var document)) return;
                var inside = new SortedSet<string>(StringComparer.Ordinal);
                Includes(document.Root, inside);
                foreach (var next in inside) Reach(next);
            }
            foreach (var pair in documents)
            {
                if (included.Contains(pair.Key)) continue;
                Reach(pair.Key);
                yield return pair.Value;
            }
            foreach (var pair in documents)
            {
                if (reached.Contains(pair.Key)) continue;
                Reach(pair.Key);
                yield return pair.Value;
            }
        }

        private static void Includes(UiElement element, ISet<string> into)
        {
            if (element.Include != null) into.Add(element.Include.Source);
            foreach (var child in element.Children) Includes(child, into);
        }
    }
}
