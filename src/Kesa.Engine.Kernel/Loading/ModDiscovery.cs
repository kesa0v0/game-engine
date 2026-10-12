using System;
using System.Collections.Generic;
using System.IO;
using Kesa.Engine.Diagnostics;

namespace Kesa.Engine.Loading
{
    public sealed class DiscoveryResult
    {
        internal DiscoveryResult(IReadOnlyList<ModManifest> mods, IReadOnlyList<string> failedIds)
        {
            Mods = mods;
            FailedIds = failedIds;
        }

        /// <summary>매니페스트를 문제없이 읽은 모드 (폴더 이름 서수 순).</summary>
        public IReadOnlyList<ModManifest> Mods { get; }

        /// <summary>id는 읽혔지만 매니페스트에 오류가 있는 모드 (서수 순).</summary>
        public IReadOnlyList<string> FailedIds { get; }
    }

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

        private static void CheckUses(string dir, ModManifest manifest, DiagnosticBag diagnostics)
        {
            foreach (var use in manifest.Uses)
            {
                string full = Path.Combine(dir, use.Path.Replace('/', Path.DirectorySeparatorChar));
                bool exists = use.Kind == UseKind.File ? File.Exists(full) : Directory.Exists(full);
                if (!exists)
                {
                    diagnostics.Error(Codes.ModBadManifest,
                        (use.Kind == UseKind.File ? "file" : "folder") + " not found: '" + use.Path + "'",
                        use.Span, mod: manifest.Id);
                }
            }
        }
    }
}
