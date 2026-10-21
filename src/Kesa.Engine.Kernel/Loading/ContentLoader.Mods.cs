using System;
using System.Collections.Generic;
using System.IO;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Schema;
using Kesa.Engine.Host;
using Kesa.Engine.Script;

namespace Kesa.Engine.Loading
{
    /// <summary>모드 단위(<c>ModUnit</c>)와 그 파일: 목록 만들기, 파싱, 실행, 함수·호스트 이름 검사, 의존 모드 실패 전파 (G16·G18·G28·G108).</summary>
    public static partial class ContentLoader
    {
        private sealed class ModUnit
        {
            private IReadOnlyList<Diagnostic> _baseDiagnostics = new List<Diagnostic>();
            private bool _baseFailed;
            private IReadOnlyList<Diagnostic>? _frozen;

            public ModUnit(ModManifest manifest)
            {
                Manifest = manifest;
                Schema = new ModSchema(manifest.Id);
            }

            public ModManifest Manifest { get; }
            public List<string> SchemaFiles { get; } = new List<string>();
            public List<string> DefFiles { get; } = new List<string>();

            /// <summary>목록의 경로(NFC) → 디스크의 실제 상대 경로. 둘이 다른 것은 유니코드 정규화뿐이다 (G213).</summary>
            public Dictionary<string, string> DiskPaths { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

            /// <summary>읽은 파일의 지문 (G212).</summary>
            public List<ModFile> Files { get; } = new List<ModFile>();

            /// <summary>파싱한 파일 (모드 폴더 기준 상대 경로 → 모듈). 회차가 바뀌어도 그대로다.</summary>
            public Dictionary<string, Module> Modules { get; } = new Dictionary<string, Module>(StringComparer.Ordinal);

            // ---- 회차마다 새로 만드는 상태 (ResetRun) ----

            public ModScope Scope { get; private set; } = new ModScope();

            /// <summary>이 모드가 정의한 로직 함수와 최상위 이름 (G108).</summary>
            public ModFunctions Functions { get; private set; } = new ModFunctions();

            public ModSchema Schema { get; private set; }
            public List<DefRecord> Defs { get; private set; } = new List<DefRecord>();

            /// <summary>②b에서 기록한 <c>patch.*</c> 호출 (G35).</summary>
            public List<PatchCall> Patches { get; private set; } = new List<PatchCall>();

            /// <summary>이 모드가 소유한 호스트 함수의 풀린 서명 (spec-gaps G215).</summary>
            public List<HostSignature> Hosts { get; set; } = new List<HostSignature>();

            /// <summary>거짓인 <c>mod_loaded</c> 가드 안에서 건너뛴 패치 호출 (G38·G46), 모드 id 순.</summary>
            public SortedDictionary<string, SkippedGuard> Skipped { get; private set; } =
                new SortedDictionary<string, SkippedGuard>(StringComparer.Ordinal);

            /// <summary>이 모드의 진단. 실행 중 진단은 모드별로 모았다가 끝에 한 번에 넘긴다.</summary>
            public DiagnosticBag Bag { get; private set; } = new DiagnosticBag();

            public bool Failed { get; set; }

            /// <summary>③ 이후 단계에서 비활성화되어 이후 회차에서 빠진 모드 (G49).</summary>
            public bool Excluded { get; private set; }

            /// <summary>파일 목록과 파싱을 마친 직후의 진단과 실패 여부를 기억한다.</summary>
            public void KeepBaseState()
            {
                _baseDiagnostics = Bag.ToSortedList();
                _baseFailed = Failed;
            }

            /// <summary>새 회차를 시작한다. 빠진 모드는 처음부터 실패이고 진단은 빠질 때 굳힌 것을 쓴다.</summary>
            public void ResetRun()
            {
                Scope = new ModScope();
                Functions = new ModFunctions();
                Schema = new ModSchema(Manifest.Id);
                Defs = new List<DefRecord>();
                Patches = new List<PatchCall>();
                Skipped = new SortedDictionary<string, SkippedGuard>(StringComparer.Ordinal);
                Hosts = new List<HostSignature>();
                Bag = new DiagnosticBag();
                Bag.AddRange(Excluded ? _frozen! : _baseDiagnostics);
                Failed = Excluded || _baseFailed;
            }

            /// <summary>
            /// 이 회차의 진단(과 의존 모드 때문에 비활성화되었다는 MOD009)을 굳히고 다음 회차에서 뺀다.
            /// </summary>
            public void Exclude(IReadOnlyList<Diagnostic> runDiagnostics)
            {
                var frozen = new List<Diagnostic>(Bag.ToSortedList());
                foreach (var d in runDiagnostics)
                    if (d.Mod == Manifest.Id && d.Code == Codes.ModDependencyFailed) frozen.Add(d);
                _frozen = frozen;
                Excluded = true;
            }
        }

        /// <summary>
        /// <c>use</c> 목록을 파일 목록으로 편다. <c>use_all("dir/")</c>는 그 아래 모든 <c>.py</c>를 하위 폴더까지
        /// 상대 경로의 서수 순으로 (spec-gaps G18). 이미 나온 파일은 다시 넣지 않는다.
        /// 경로가 <see cref="SchemaFolder"/>로 시작하면 스키마 파일, 아니면 Def 파일이고 각각 목록 순서를 지킨다.
        /// </summary>
        private static void ListFiles(string modsRoot, ModUnit u)
        {
            string modDir = Path.Combine(modsRoot, u.Manifest.Directory);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var use in u.Manifest.Uses)
            {
                // 경로는 NFC로 다루고 디스크의 실제 이름은 열 때만 쓴다 (G213)
                var files = new List<string>();
                var disk = new Dictionary<string, string>(StringComparer.Ordinal);
                string? found = ModDiscovery.FindOnDisk(modDir, use.Path, use.Kind == UseKind.File, out _);
                if (found == null) continue; // ①에서 보고됨
                if (use.Kind == UseKind.File)
                {
                    string rel = ModDiscovery.Nfc(found);
                    files.Add(rel);
                    disk[rel] = found;
                }
                else
                {
                    string dir = Path.Combine(modDir, found.Replace('/', Path.DirectorySeparatorChar));
                    foreach (var f in Directory.GetFiles(dir, "*.py", SearchOption.AllDirectories))
                    {
                        string actual = Path.GetRelativePath(modDir, f).Replace(Path.DirectorySeparatorChar, '/');
                        if (!actual.EndsWith(".py", StringComparison.Ordinal)) continue; // "*.py"는 ".pyi"도 잡는다
                        string rel = ModDiscovery.Nfc(actual);
                        if (rel == ModDiscovery.ManifestFileName) continue;
                        if (disk.ContainsKey(rel))
                        {
                            u.Bag.Error(Codes.LodBadUse, "'" + rel + "' is on disk twice, as names that differ only in Unicode " +
                                "normalization", use.Span, u.Manifest.Id, hint: "keep one of them");
                            continue;
                        }
                        files.Add(rel);
                        disk[rel] = actual;
                    }
                    files.Sort(StringComparer.Ordinal);
                }

                foreach (var rel in files)
                {
                    if (!u.DiskPaths.ContainsKey(rel)) u.DiskPaths[rel] = disk[rel];
                    if (rel == ModDiscovery.ManifestFileName)
                    {
                        u.Bag.Error(Codes.LodBadUse, "mod.py cannot be listed in use()", use.Span, u.Manifest.Id);
                        continue;
                    }
                    if (!seen.Add(rel)) continue;
                    if (!rel.StartsWith(SchemaFolder, StringComparison.Ordinal) &&
                        rel.StartsWith(SchemaFolder, StringComparison.OrdinalIgnoreCase))
                    {
                        // 대소문자를 무시하는 파일 시스템에서만 다르게 동작하지 않도록 (G7과 같은 이유)
                        u.Bag.Error(Codes.LodBadUse, "the schema folder must be named exactly 'schema/'", use.Span,
                            u.Manifest.Id, hint: "rename '" + rel.Substring(0, SchemaFolder.Length) + "' to 'schema/'");
                        continue;
                    }
                    if (rel.StartsWith(SchemaFolder, StringComparison.Ordinal)) u.SchemaFiles.Add(rel);
                    else u.DefFiles.Add(rel);
                }
            }
            if (u.Bag.HasErrors) u.Failed = true;
        }

        /// <summary>
        /// 모드의 모든 파일을 읽고 파싱한다. 하나라도 읽기·구문 오류가 있으면 모드는 실패이고 어떤 파일도 실행하지 않는다:
        /// 모드는 어차피 비활성화되고, 실행하면 뒤따르는 오류만 늘어난다.
        /// </summary>
        private static void ParseFiles(string modsRoot, ModUnit u)
        {
            var all = new List<string>(u.SchemaFiles);
            all.AddRange(u.DefFiles);
            // 내용 지문 (G212): mod.py도 읽은 파일이다(use 순서가 거기 있다)
            try
            {
                var manifest = File.ReadAllBytes(Path.Combine(modsRoot, u.Manifest.Directory, ModDiscovery.ManifestFileName));
                u.Files.Add(new ModFile(ModDiscovery.ManifestFileName, ModFileKind.Manifest, ContentHash.OfFile(manifest)));
            }
            catch (IOException e)
            {
                u.Bag.Error(Codes.LodBadUse, "cannot read file: " + e.Message,
                    new SourceSpan(u.Manifest.Directory + "/" + ModDiscovery.ManifestFileName, 0, 0), u.Manifest.Id);
            }
            foreach (var relativePath in all)
            {
                string path = Path.Combine(modsRoot, u.Manifest.Directory,
                    u.DiskPaths[relativePath].Replace('/', Path.DirectorySeparatorChar));
                string file = u.Manifest.Directory + "/" + relativePath;
                string text;
                try
                {
                    var bytes = File.ReadAllBytes(path);
                    u.Files.Add(new ModFile(relativePath, u.SchemaFiles.Contains(relativePath) ? ModFileKind.Schema : ModFileKind.Defs,
                        ContentHash.OfFile(bytes)));
                    // File.ReadAllText(path, UTF8)와 같게 읽는다 (BOM으로 인코딩을 알아냄)
                    using (var reader = new StreamReader(new MemoryStream(bytes), System.Text.Encoding.UTF8, true))
                        text = reader.ReadToEnd();
                }
                catch (IOException e)
                {
                    u.Bag.Error(Codes.LodBadUse, "cannot read file: " + e.Message, new SourceSpan(file, 0, 0),
                        u.Manifest.Id);
                    continue;
                }

                var parseBag = new DiagnosticBag();
                u.Modules[relativePath] = Parser.Parse(text, file, parseBag);
                foreach (var d in parseBag.ToSortedList())
                    u.Bag.Add(new Diagnostic(d.Code, d.Severity, d.Message, u.Manifest.Id, d.Span, d.Hint, d.Related));
            }
            if (u.Bag.HasErrors) u.Failed = true;
        }

        private static void Execute(ModUnit u, string relativePath, IBuiltins builtins, bool allowConditions = true) =>
            new Interpreter(u.Scope, builtins, new CallContext(u.Manifest.Id, u.Bag, u.Skipped, u.Functions), allowConditions)
                .Run(u.Modules[relativePath]);

        /// <summary>
        /// 모드의 모든 파일을 실행한 뒤 함수 본문의 이름을 본다 (spec-gaps G108). 스키마 파일의 함수가 Def 파일의 이름을
        /// 써도 되므로 ②b 끝에서 한다. 로드 시점 이름(<c>field</c>, <c>patch</c>, Def 생성자 등)은 따로 안내한다.
        /// </summary>
        private static void CheckFunctionNames(ModUnit u, DefBuiltins builtins, HostNames hostNames, ModNames modNames) =>
            FunctionNameChecker.Check(u.Functions, u.Scope,
                name => name == Interpreter.InheritName || builtins.Lookup(name) != null || builtins.Explain(name) != null,
                new CallContext(u.Manifest.Id, u.Bag), builtins.IsVisibleRuntimeSchemaName,
                // Explain은 Lookup이 못 찾은 이름에만 맞다 (보이는지는 보지 않는다)
                name => builtins.Lookup(name) == null && builtins.Explain(name) is { } e && e.Code == Codes.SchNotVisible
                    ? e.Message
                    : null,
                builtins.RuntimeSchemaNames, hostNames, builtins.IsVisibleDefTypeName, modNames);

        /// <summary>
        /// 이 모드의 함수 본문에서 <c>모드id.이름</c>으로 쓸 수 있는 모드 (G309): 자신과 필수 의존(전이) 중 아직 실패하지 않은 모드.
        /// 다른 활성 모드는 "의존을 더하라"는 안내를 위해 id만 남긴다. 의존 대상은 로드 순서상 먼저 실행되어 이름이 다 묶여 있다.
        /// </summary>
        private static ModNames ModNamesFor(ModUnit u, List<ModUnit> units, ModVisibility visibility)
        {
            var visible = new List<KeyValuePair<string, ModScope>>();
            var hidden = new List<string>();
            foreach (var owner in units)
            {
                if (owner.Excluded) continue;
                if (!visibility.CanSee(u.Manifest.Id, owner.Manifest.Id)) hidden.Add(owner.Manifest.Id);
                else if (!owner.Failed) visible.Add(new KeyValuePair<string, ModScope>(owner.Manifest.Id, owner.Scope));
            }
            return new ModNames(visible, hidden);
        }

        /// <summary>
        /// 이 모드의 함수 본문에서 보이는 호스트 함수 (G218): 소유 모드가 자신이거나 필수 의존(전이)인 함수. 다른 활성 모드의 함수는
        /// "의존을 더하라"는 안내를 위해 소유 모드 id만 남긴다.
        /// </summary>
        private static HostNames HostNamesFor(ModUnit u, List<ModUnit> units, ModVisibility visibility, HostFunctions? host)
        {
            if (host == null) return HostNames.None;
            var visible = new List<KeyValuePair<string, bool>>();
            var hidden = new List<string>();
            foreach (var owner in units)
            {
                if (owner.Excluded) continue;
                var functions = host.Of(owner.Manifest.Id);
                if (functions.Count == 0) continue;
                if (!visibility.CanSee(u.Manifest.Id, owner.Manifest.Id))
                {
                    hidden.Add(owner.Manifest.Id);
                    continue;
                }
                foreach (var f in functions) visible.Add(new KeyValuePair<string, bool>(f.Name, f.Effect == HostEffect.Writes));
            }
            return new HostNames(visible, hidden);
        }

        /// <summary>풀린 호스트 함수 서명을 전체 이름으로 찾는다 (Def 값 자리의 호스트 함수, G291). 아직 풀리지 않았으면 null.</summary>
        private static HostSignature? HostSignatureNamed(List<ModUnit> units, string fullName)
        {
            foreach (var owner in units)
                foreach (var signature in owner.Hosts)
                    if (signature.Function.Name == fullName) return signature;
            return null;
        }

        /// <summary>
        /// 모드가 최상위에 호스트 이름 공간이나 보이는 모드 id와 같은 이름을 묶으면 그 이름들이 가려지므로 로드 오류다 (G218·G309).
        /// 자기 모드의 호스트 함수 경로 첫 조각과 같은 이름도 <c>모드id.이름</c>이 둘을 가리키므로 로드 오류다 (G309).
        /// </summary>
        private static void CheckHostShadowing(ModUnit u, HostNames hostNames, ModNames modNames)
        {
            string self = u.Manifest.Id;
            foreach (var name in u.Scope.Names)
            {
                u.Scope.TryGet(name, out _, out var span);
                if (hostNames.IsRoot(name))
                    u.Bag.Error(Codes.HostName,
                        "the top-level name '" + name + "' hides the host functions of mod '" + name + "'", span, self,
                        "rename it (spec-gaps G218)");
                else if (modNames.IsRoot(name))
                    u.Bag.Error(Codes.EvlNameTaken,
                        "the top-level name '" + name + "' hides the names of mod '" + name + "' (" + name + ".…)", span, self,
                        "rename it (spec-gaps G309)");
                string path = self + "." + name;
                if (hostNames.IsFunction(path) || hostNames.IsNamespace(path))
                    u.Bag.Error(Codes.HostName,
                        "'" + path + "' is both a host function" + (hostNames.IsFunction(path) ? "" : " group") +
                        " and this mod's top-level name '" + name + "'", span, self,
                        "rename one of them (spec-gaps G309)");
            }
        }

        /// <summary>
        /// 필수 의존 중 실패한 모드가 있으면 이 모드를 비활성화한다 (MOD009). 로드 순서상 의존 대상이 앞에 있으므로
        /// 순서대로 부르면 전이적으로 퍼진다.
        /// </summary>
        private static bool FailDependents(ModUnit u, List<ModUnit> units, DiagnosticBag diagnostics)
        {
            if (u.Failed) return true;
            foreach (var dep in u.Manifest.Dependencies)
            {
                foreach (var other in units)
                {
                    if (other.Manifest.Id != dep.Id || !other.Failed) continue;
                    diagnostics.Error(Codes.ModDependencyFailed,
                        "mod '" + u.Manifest.Id + "' was disabled because its dependency '" + dep.Id + "' failed to load",
                        dep.Span, mod: u.Manifest.Id);
                    u.Failed = true;
                    return true;
                }
            }
            return false;
        }
    }
}
