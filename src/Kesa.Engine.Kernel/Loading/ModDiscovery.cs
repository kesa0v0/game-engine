using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Kesa.Engine.Diagnostics;

namespace Kesa.Engine.Loading
{
    /// <summary>
    /// 모드 루트의 바로 아래 폴더마다 <c>mod.py</c>를 찾아 읽는다 (spec S1 ① 발견).
    /// 폴더와 파일은 서수 순으로 다뤄 결과가 파일 시스템 순서와 무관하다.
    /// </summary>
    public static class ModDiscovery
    {
        public const string ManifestFileName = "mod.py";

        public static DiscoveryResult Discover(string modsRoot, DiagnosticBag diagnostics)
        {
            var mods = new List<ModManifest>();
            var failed = new SortedSet<string>(StringComparer.Ordinal);
            var dirs = new List<string>();
            foreach (var d in Directory.GetDirectories(modsRoot)) dirs.Add(d);
            dirs.Sort(StringComparer.Ordinal);

            foreach (var dir in dirs)
            {
                string name = Path.GetFileName(dir);
                if (name.Length > 0 && name[0] == '.') continue;

                string? manifestPath = FindManifest(dir, name, diagnostics);
                if (manifestPath == null) continue;

                string text = File.ReadAllText(manifestPath, System.Text.Encoding.UTF8);
                var local = new DiagnosticBag();
                var manifest = ManifestReader.Read(text, name, local, out var declaredId);
                if (manifest != null) CheckUses(dir, manifest, local);
                diagnostics.AddRange(local.ToSortedList());

                if (manifest != null && !local.HasErrors) mods.Add(manifest);
                else if (declaredId != null) failed.Add(declaredId);
            }
            return new DiscoveryResult(mods, new List<string>(failed));
        }

        /// <summary>대소문자까지 정확히 'mod.py'인 파일을 찾는다. 대소문자만 다른 파일은 오류.</summary>
        private static string? FindManifest(string dir, string name, DiagnosticBag diagnostics)
        {
            string? exact = null;
            string? caseMismatch = null;
            foreach (var f in Directory.GetFiles(dir))
            {
                string fileName = Path.GetFileName(f);
                if (string.Equals(fileName, ManifestFileName, StringComparison.Ordinal)) exact = f;
                else if (string.Equals(fileName, ManifestFileName, StringComparison.OrdinalIgnoreCase)) caseMismatch = fileName;
            }

            if (exact != null) return exact;

            var span = new SourceSpan(name + "/" + ManifestFileName, 0, 0);
            if (caseMismatch != null)
            {
                diagnostics.Error(Codes.ModNoManifest, "manifest must be named exactly 'mod.py'", span,
                    hint: "rename '" + caseMismatch + "' to 'mod.py'");
            }
            else
            {
                diagnostics.Warning(Codes.ModNoManifest, "folder '" + name + "' has no mod.py and was skipped", span);
            }
            return null;
        }

        /// <summary>
        /// <c>use</c> 경로가 대소문자까지 정확히 있는지 본다. 대소문자만 다르면 오류다: 대소문자를 무시하는 파일 시스템에서만
        /// 통과하면 같은 모드가 기계마다 다르게 로드된다 (spec-gaps G7).
        /// </summary>
        private static void CheckUses(string dir, ModManifest manifest, DiagnosticBag diagnostics)
        {
            foreach (var use in manifest.Uses)
            {
                string what = use.Kind == UseKind.File ? "file" : "folder";
                string? actual = FindOnDisk(dir, use.Path, use.Kind == UseKind.File, out var ambiguous);
                if (ambiguous != null)
                {
                    diagnostics.Error(Codes.ModBadManifest,
                        what + " '" + use.Path + "' matches '" + ambiguous + "' and another name on disk that differ only in " +
                        "Unicode normalization", use.Span, mod: manifest.Id, hint: "keep one of them");
                }
                else if (actual == null)
                {
                    diagnostics.Error(Codes.ModBadManifest, what + " not found: '" + use.Path + "'", use.Span,
                        mod: manifest.Id);
                }
                else if (Nfc(actual) != Nfc(use.Path))
                {
                    diagnostics.Error(Codes.ModBadManifest,
                        what + " '" + use.Path + "' differs in letter case from the one on disk", use.Span,
                        mod: manifest.Id, hint: "did you mean '" + Nfc(actual) + "'?");
                }
            }
        }

        /// <summary>
        /// 유니코드 NFC 정규화 (spec-gaps G213). 같은 글자를 완성형(NFC)과 자모 분리형(NFD, macOS에서 흔함)으로 쓸 수 있어
        /// 경로를 비교·기록할 때는 늘 NFC로 맞춘다. 파일을 열 때만 디스크의 실제 이름을 쓴다.
        /// </summary>
        internal static string Nfc(string s) => s.Normalize(NormalizationForm.FormC);

        /// <summary>
        /// 상대 경로(<c>/</c> 구분)를 한 단계씩 디스크의 이름과 맞춰 실제 이름의 경로를 돌려준다. 단계마다 NFC로 같은 이름을
        /// 먼저 찾고(G213), 없으면 대소문자만 다른 이름(서수 순 첫째)을 쓴다. 어느 쪽도 없으면 null.
        /// NFC로 같은 이름이 한 폴더에 둘 이상이면 어느 것인지 모호하므로 null이고 <paramref name="ambiguous"/>에 그 이름을 둔다.
        /// </summary>
        internal static string? FindOnDisk(string dir, string relativePath, bool isFile, out string? ambiguous)
        {
            ambiguous = null;
            var segments = relativePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            var actual = new List<string>();
            string current = dir;
            for (int i = 0; i < segments.Length; i++)
            {
                bool last = i == segments.Length - 1;
                if (!Directory.Exists(current)) return null;
                var entries = last && isFile ? Directory.GetFiles(current) : Directory.GetDirectories(current);
                var names = new List<string>();
                foreach (var e in entries) names.Add(Path.GetFileName(e));
                names.Sort(StringComparer.Ordinal);

                string wanted = Nfc(segments[i]);
                string? match = null;
                foreach (var n in names)
                {
                    if (!string.Equals(Nfc(n), wanted, StringComparison.Ordinal)) continue;
                    if (match != null)
                    {
                        ambiguous = string.Join("/", actual) + (actual.Count > 0 ? "/" : "") + wanted;
                        return null;
                    }
                    match = n;
                }
                if (match == null)
                {
                    foreach (var n in names)
                    {
                        if (!string.Equals(Nfc(n), wanted, StringComparison.OrdinalIgnoreCase)) continue;
                        match = n;
                        break;
                    }
                }
                if (match == null) return null;
                actual.Add(match);
                current = Path.Combine(current, match);
            }
            return string.Join("/", actual);
        }
    }
}
