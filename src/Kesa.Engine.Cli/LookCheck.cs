using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Loading;
using Kesa.Engine.Ui;

namespace Kesa.Engine.Cli
{
    /// <summary>
    /// <c>engine check</c>의 그림·공간·보는 법 파일 검사 (G480). Unity 호스트와 같은 <see cref="LookLibrary"/>로 읽는다.
    /// 게임이 C#으로 등록한 종류는 알 수 없으므로 경고로만 알린다.
    /// </summary>
    internal static class LookCheck
    {
        private static readonly string[] Extensions = { ".look.xml", ".space.xml", ".view.xml" };

        // Unity 패키지의 기본 모델 로더(GltfModelLoader)가 맡는 확장자
        private static readonly string[] StandardModelFormats = { ".glb", ".gltf" };

        public static void Run(ContentLoadResult content, DiagnosticBag bag)
        {
            var kinds = LookKinds.Standard();
            kinds.AllowUnknown = true;
            foreach (var format in StandardModelFormats) kinds.AddModelFormat(format);

            var folders = content.Mods.Select(m => Path.Combine(content.ModsRoot, m.Directory)).ToList();
            var library = LookLibrary.Load(Sources(content), kinds, path => Find(path, folders) != null);
            foreach (var problem in library.Problems)
            {
                var span = SpanOf(problem.Place);
                string? mod = content.Mods.FirstOrDefault(m => span.File.StartsWith(m.Directory + "/", StringComparison.Ordinal))?.Id;
                if (problem.Unverified) bag.Warning(Codes.LookUnverified, problem.Message, span, mod);
                else bag.Error(Codes.LookProblem, problem.Message, span, mod);
            }
        }

        // 진단의 경로는 모드 루트 기준이라 호스트와 달리 모드 id가 아니라 폴더로 시작한다
        private static IEnumerable<LookSource> Sources(ContentLoadResult content)
        {
            foreach (var mod in content.Mods)
            {
                string folder = Path.Combine(content.ModsRoot, mod.Directory);
                var files = Directory.GetFiles(folder, "*.xml", SearchOption.AllDirectories)
                    .Select(f => Path.GetRelativePath(folder, f).Replace('\\', '/'))
                    .Where(f => Extensions.Any(e => f.EndsWith(e, StringComparison.OrdinalIgnoreCase)))
                    .OrderBy(f => f, StringComparer.Ordinal);
                foreach (var file in files) yield return new LookSource(mod.Id, mod.Directory + "/" + file, File.ReadAllText(Path.Combine(folder, file)));
            }
        }

        // 호스트의 ModFolderAssets.Find와 같은 규칙: 모드 폴더 안의 상대 경로만, 나중 모드부터 (G335)
        internal static string? Find(string path, IReadOnlyList<string> folders)
        {
            if (path.Length == 0 || Path.IsPathRooted(path) || path.IndexOf(':') >= 0) return null;
            if (path.Split('/', '\\').Contains("..")) return null;
            for (int i = folders.Count - 1; i >= 0; i--)
            {
                string full = Path.Combine(folders[i], path);
                if (File.Exists(full)) return full;
            }
            return null;
        }

        internal static SourceSpan SpanOf(string place)
        {
            int colon = place.LastIndexOf(':');
            return colon >= 0 && int.TryParse(place.Substring(colon + 1), out int line)
                ? new SourceSpan(place.Substring(0, colon), line, 1)
                : new SourceSpan(place, 1, 1);
        }
    }
}
