using System;
using System.Collections.Generic;
using System.IO;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Schema;
using Kesa.Engine.Script;

namespace Kesa.Engine.Loading
{
    /// <summary>②의 결과: 완성된 스키마와 등록된 Def.</summary>
    public sealed class ContentLoadResult
    {
        internal ContentLoadResult(IReadOnlyList<ModManifest> mods, IReadOnlyList<string> disabled, SchemaSet schema,
            DefRegistry defs, IReadOnlyList<PatchCall> patches, IReadOnlyList<ResolvedPatch> resolved, PatchLog log,
            ResolvedDefs expanded, DefSet frozen, DefinitionSnapshot firstRound,
            IReadOnlyDictionary<string, ModScope> scopes)
        {
            Scopes = scopes;
            FirstRound = firstRound;
            Expanded = expanded;
            Frozen = frozen;
            Mods = mods;
            Disabled = disabled;
            Schema = schema;
            Defs = defs;
            Patches = patches;
            Resolved = resolved;
            PatchLog = log;
        }

        /// <summary>②까지 살아남은 모드 (로드 순서).</summary>
        public IReadOnlyList<ModManifest> Mods { get; }

        /// <summary>①과 ②에서 비활성화된 모드 id (서수 순). 이유는 진단에 있다.</summary>
        public IReadOnlyList<string> Disabled { get; }

        public SchemaSet Schema { get; }
        public DefRegistry Defs { get; }

        /// <summary>
        /// ②b가 기록한 패치 호출 (spec-gaps G35). 살아남은 모드의 것만, 모드 로드 순서 → 호출 순서로. 아직 적용하지 않았다.
        /// </summary>
        public IReadOnlyList<PatchCall> Patches { get; }

        /// <summary>③의 대상·경로 검사를 통과한 패치 호출과 그 대상 (<see cref="Patches"/>와 같은 순서). 아직 적용하지 않았다.</summary>
        public IReadOnlyList<ResolvedPatch> Resolved { get; }

        /// <summary>패치 변경 이력 (spec-gaps G45). 모든 검사를 통과한 호출이 적용 순서대로 들어 있다.</summary>
        public PatchLog PatchLog { get; }

        /// <summary>④가 상속을 펼친 Def들 (spec-gaps G52). 패치는 아직 반영하지 않았다.</summary>
        public ResolvedDefs Expanded { get; }

        /// <summary>동결된 Def 집합 (spec-gaps G69·G70): 검증을 마친 최종 값, 기본값까지 채움, `abstract` 제외.</summary>
        public DefSet Frozen { get; }

        /// <summary>
        /// 첫 회차의 ②a·②b 결과 (spec-gaps G83). ③~⑥에서 비활성화된 모드도 들어 있어 그 모드의 작성용 스텁을 만드는 데 쓴다.
        /// 편집기용 추정일 뿐이고 로드의 판정은 아니다.
        /// </summary>
        public DefinitionSnapshot FirstRound { get; }

        /// <summary>
        /// 살아남은 모드별 최상위 이름 공간(모드 id → 이름들). 런타임에 함수 본문이 같은 모드의 함수·상수를 찾는 데 쓴다
        /// (spec-gaps G105).
        /// </summary>
        public IReadOnlyDictionary<string, ModScope> Scopes { get; }
    }

    /// <summary>한 회차의 ②a·②b가 남긴 것: 살아남은 모드(로드 순서), 완성된 스키마, 등록된 Def.</summary>
    public sealed class DefinitionSnapshot
    {
        internal DefinitionSnapshot(IReadOnlyList<ModManifest> mods, SchemaSet schema, DefRegistry defs)
        {
            Mods = mods;
            Schema = schema;
            Defs = defs;
        }

        public IReadOnlyList<ModManifest> Mods { get; }
        public SchemaSet Schema { get; }
        public DefRegistry Defs { get; }
    }

    /// <summary>
    /// 스키마·정의 단계 (spec S1 ②).
    /// ②a: 모든 모드의 스키마 파일(<c>use</c> 목록 중 <see cref="SchemaFolder"/> 아래)만 모드 순서대로 실행해 타입을 완성한다.
    /// ②b: 그다음 나머지 파일(Def 파일)을 실행해 Def를 등록만 한다. 이때 타입은 고정이다 (schema-design 4.1).
    /// 실패한 모드는 통째로 비활성화하고 그 모드에 필수로 의존하는 모드도 비활성화한다 (spec S1 오류 정책).
    /// </summary>
    public static class ContentLoader
    {
        /// <summary>이 폴더 아래 파일은 스키마 파일이다 (spec-gaps G16).</summary>
        public const string SchemaFolder = "schema/";

        public static ContentLoadResult Load(string modsRoot, LoadOrder order, DiagnosticBag diagnostics)
        {
            var units = new List<ModUnit>();
            foreach (var m in order.Mods) units.Add(new ModUnit(m));
            var disabled = new SortedSet<string>(order.Disabled, StringComparer.Ordinal);

            // 파일 목록과 파싱. 모든 파일(②b 파일 포함)을 ②a 전에 파싱해 구문 오류가 있는 모드를 먼저 뺀다:
            // 그래야 ②a의 충돌 판정이 상대의 구문 오류가 어느 파일에 있는지에 따라 달라지지 않는다 (spec-gaps G28).
            // 파싱 결과는 회차가 바뀌어도 그대로 쓴다.
            foreach (var u in units)
            {
                ListFiles(modsRoot, u);
                if (!u.Failed) ParseFiles(modsRoot, u);
                u.KeepBaseState();
            }

            // 파이프라인을 활성 모드 집합으로 돌린다. ③ 이후 단계에서 모드가 비활성화되면 그 모드와 의존 모드를 빼고
            // ②a부터 다시 돌린다 (spec-gaps G49): 비활성화된 모드의 `extend`가 스키마에 남거나 이미 참으로 평가된
            // `mod_loaded` 가드가 낡은 채로 남지 않게 한다. 집합이 줄어드는 방향이라 반복은 끝난다.
            DefinitionSnapshot? firstRound = null;
            while (true)
            {
                foreach (var u in units) u.ResetRun();
                var runDiagnostics = new DiagnosticBag();
                var run = RunDefinitionStages(units, order, runDiagnostics);
                // ③ 이후에 빠질 모드도 아직 들어 있을 때 남긴다 (G83)
                firstRound ??= new DefinitionSnapshot(SurvivingManifests(units), run.Schema, run.Defs);
                if (ExcludeFailedAtPatchStage(units, run, order, runDiagnostics)) continue;

                diagnostics.AddRange(runDiagnostics.ToSortedList());
                var mods = new List<ModManifest>();
                var patches = new List<PatchCall>();
                var scopes = new SortedDictionary<string, ModScope>(StringComparer.Ordinal);
                foreach (var u in units)
                {
                    diagnostics.AddRange(u.Bag.ToSortedList());
                    if (u.Failed)
                    {
                        disabled.Add(u.Manifest.Id);
                        continue;
                    }
                    mods.Add(u.Manifest);
                    patches.AddRange(u.Patches);
                    scopes.Add(u.Manifest.Id, u.Scope);
                }
                return new ContentLoadResult(mods, new List<string>(disabled), run.Schema, run.Defs, patches, run.Resolved,
                    run.Log, run.Expanded, DefSet.Freeze(run.Expanded), firstRound, scopes);
            }
        }

        private static List<ModManifest> SurvivingManifests(List<ModUnit> units)
        {
            var mods = new List<ModManifest>();
            foreach (var u in units)
                if (!u.Failed) mods.Add(u.Manifest);
            return mods;
        }

        /// <summary>②a·②b가 한 회차에 만든 것.</summary>
        private sealed class DefinitionRun
        {
            public DefinitionRun(SchemaSet schema, DefRegistry defs, ModVisibility visibility)
            {
                Schema = schema;
                Defs = defs;
                Visibility = visibility;
            }

            public SchemaSet Schema { get; }
            public DefRegistry Defs { get; }
            public ModVisibility Visibility { get; }

            /// <summary>③이 검사를 통과시킨 패치 (③을 돌린 뒤에 채워진다).</summary>
            public IReadOnlyList<ResolvedPatch> Resolved { get; set; } = new List<ResolvedPatch>();

            /// <summary>③이 만든 변경 이력 (G45).</summary>
            public PatchLog Log { get; set; } = new PatchLog(new List<ResolvedPatch>());

            /// <summary>④가 상속을 펼친 Def들 (G52).</summary>
            public ResolvedDefs Expanded { get; set; } = new ResolvedDefs();
        }

        /// <summary>
        /// ②a(스키마 파일 실행과 타입 완성)와 ②b(Def 파일 실행과 Def 등록). 이전 회차에서 비활성화된 모드는 처음부터 실패로 본다.
        /// 이 안의 실패는 G28의 고정점으로 푼다. <paramref name="runDiagnostics"/>는 이번 회차에만 속한 진단(MOD009)이다.
        /// </summary>
        private static DefinitionRun RunDefinitionStages(List<ModUnit> units, LoadOrder order, DiagnosticBag runDiagnostics)
        {
            var active = new List<ModManifest>();
            foreach (var u in units)
                if (!u.Excluded) active.Add(u.Manifest);
            var visibility = new ModVisibility(active);

            // ②a 실행: 의존 대상이 먼저 실행되므로, 실패한 의존이 있으면 실행하지 않고 비활성화한다
            var declaredTypes = new HashSet<string>(StringComparer.Ordinal);
            var declaredComponents = new HashSet<string>(StringComparer.Ordinal);
            foreach (var u in units)
            {
                if (u.Failed || FailDependents(u, units, runDiagnostics)) continue;
                var unit = u;
                var builtins = new SchemaBuiltins(u.Schema,
                    name => declaredTypes.Contains(name) || unit.Schema.Types.Exists(t => t.Name == name),
                    name => declaredComponents.Contains(name) || unit.Schema.Components.Exists(c => c.Name == name));
                foreach (var file in u.SchemaFiles) Execute(u, file, builtins, allowConditions: false);
                foreach (var t in u.Schema.Types) declaredTypes.Add(t.Name);
                foreach (var c in u.Schema.Components) declaredComponents.Add(c.Name);
                if (u.Bag.HasErrors) u.Failed = true;
            }

            // ②a 끝: 타입 완성. 실패한 모드를 빼고 다시 만든다 (고정점)
            var schema = BuildSchema(units, visibility, runDiagnostics);

            // ②b 실행
            int survivorsBefore = CountSurvivors(units);
            foreach (var u in units)
            {
                if (u.Failed || FailDependents(u, units, runDiagnostics)) continue;
                // mod_loaded: 설치되어 있고 지금까지 실패하지 않은 모드 (spec-gaps G8, G46)
                var builtins = new DefBuiltins(schema, visibility, u.Manifest,
                    id => units.Exists(o => o.Manifest.Id == id && !o.Failed), u.Defs, u.Patches);
                foreach (var file in u.DefFiles) Execute(u, file, builtins);
                CheckFunctionNames(u, builtins);
                // 로드되지 않은 모드의 가드 안에서 건너뛴 패치는 조용히 사라지지 않게 알린다 (G38)
                foreach (var skipped in u.Skipped)
                {
                    u.Bag.Info(Codes.PatSkipped,
                        "mod '" + skipped.Key + "' is not loaded, so " + skipped.Value.Count + " patch call" +
                        (skipped.Value.Count == 1 ? " was" : "s were") + " not applied",
                        skipped.Value.First, u.Manifest.Id);
                }
                if (u.Bag.HasErrors) u.Failed = true;
            }

            // ②b 끝: Def 등록 (고정점). 모드가 더 빠졌으면 스키마도 그 모드의 확장 없이 다시 만든다.
            var defs = BuildDefs(units, visibility, order, runDiagnostics);
            if (CountSurvivors(units) != survivorsBefore)
            {
                schema = BuildSchema(units, visibility, runDiagnostics);
                Rebind(defs, schema);
            }
            return new DefinitionRun(schema, defs, visibility);
        }

        /// <summary>
        /// ③: 기록한 패치 호출을 검사한다. 오류가 난 모드는 G28과 같은 규칙(자기 오류가 먼저, 충돌 오류는 그런 모드가 없을 때만)으로
        /// 비활성화하고, 그 모드와 의존 모드를 다음 회차에서 뺀다 (G49). 뺀 모드가 있으면 true (다시 돌려야 함).
        /// </summary>
        private static bool ExcludeFailedAtPatchStage(List<ModUnit> units, DefinitionRun run, LoadOrder order,
            DiagnosticBag runDiagnostics)
        {
            var stage = new StageDiagnostics(SurvivorIds(units));
            var mods = new List<KeyValuePair<ModManifest, IReadOnlyList<PatchCall>>>();
            foreach (var u in units)
                if (!u.Failed) mods.Add(new KeyValuePair<ModManifest, IReadOnlyList<PatchCall>>(u.Manifest, u.Patches));
            run.Resolved = PatchResolver.Resolve(mods, run.Defs, run.Schema, run.Visibility, stage);
            var conflicted = PatchConflicts.Check(run.Resolved, order, stage);
            run.Log = new PatchLog(run.Resolved);

            // ④ 상속 펼치기. 오류는 ③과 같은 통로로 모아 모드를 함께 정산한다
            var environments = new Dictionary<string, ModEnvironment>(StringComparer.Ordinal);
            foreach (var u in units)
            {
                if (u.Failed) continue;
                var unit = u;
                var builtins = new DefBuiltins(run.Schema, run.Visibility, u.Manifest,
                    id => units.Exists(o => o.Manifest.Id == id && !o.Failed), new List<DefRecord>(), new List<PatchCall>());
                environments[u.Manifest.Id] = new ModEnvironment(unit.Scope, builtins);
            }
            run.Expanded = InheritanceExpander.Expand(run.Defs, run.Schema, run.Visibility, mod => environments[mod], stage, run.Log, conflicted);
            Linker.Check(run.Expanded, run.Defs, run.Schema, run.Visibility, id => units.Find(u => u.Manifest.Id == id)?.Manifest, stage);
            Validator.Check(run.Expanded, run.Schema, run.Visibility, stage);

            var failedBefore = new HashSet<string>(StringComparer.Ordinal);
            foreach (var u in units)
                if (u.Failed) failedBefore.Add(u.Manifest.Id);
            if (!Settle(units, stage, runDiagnostics)) return false;

            var dependencyFailures = runDiagnostics.ToSortedList();
            foreach (var u in units)
            {
                if (!u.Failed || failedBefore.Contains(u.Manifest.Id)) continue;
                u.Exclude(dependencyFailures);
            }
            return true;
        }

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
                var files = new List<string>();
                if (use.Kind == UseKind.File)
                {
                    files.Add(use.Path);
                }
                else
                {
                    string dir = Path.Combine(modDir, use.Path.Replace('/', Path.DirectorySeparatorChar));
                    if (!Directory.Exists(dir)) continue; // ①에서 보고됨
                    foreach (var f in Directory.GetFiles(dir, "*.py", SearchOption.AllDirectories))
                    {
                        string rel = Path.GetRelativePath(modDir, f).Replace(Path.DirectorySeparatorChar, '/');
                        if (rel == ModDiscovery.ManifestFileName) continue;
                        if (!rel.EndsWith(".py", StringComparison.Ordinal)) continue; // "*.py"는 ".pyi"도 잡는다
                        files.Add(rel);
                    }
                    files.Sort(StringComparer.Ordinal);
                }

                foreach (var rel in files)
                {
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
            foreach (var relativePath in all)
            {
                string path = Path.Combine(modsRoot, u.Manifest.Directory,
                    relativePath.Replace('/', Path.DirectorySeparatorChar));
                string file = u.Manifest.Directory + "/" + relativePath;
                string text;
                try
                {
                    text = File.ReadAllText(path, System.Text.Encoding.UTF8);
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
        private static void CheckFunctionNames(ModUnit u, DefBuiltins builtins) =>
            FunctionNameChecker.Check(u.Functions, u.Scope,
                name => name == Interpreter.InheritName || builtins.Lookup(name) != null || builtins.Explain(name) != null,
                new CallContext(u.Manifest.Id, u.Bag));

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

        /// <summary>
        /// 스키마를 다시 만들었으면 Def가 새 타입 객체를 가리키게 한다. 남은 Def의 타입과 컴포넌트는 남은 모드의 것이므로
        /// (실패한 모드의 것을 쓰는 모드는 함께 실패) 이름으로 항상 찾을 수 있다.
        /// </summary>
        private static void Rebind(DefRegistry defs, SchemaSet schema)
        {
            foreach (var record in defs.Defs)
            {
                for (var r = record; r != null; r = r.Replaced)
                {
                    schema.TryGetType(r.Type.Name, out var type);
                    r.Type = type;
                    foreach (var f in r.Fields) Rebind(f.Value, schema);
                }
            }
        }

        private static void Rebind(Value value, SchemaSet schema)
        {
            switch (value)
            {
                case ComponentValue cv:
                    schema.TryGetComponent(cv.Component.Name, out var component);
                    cv.Component = component;
                    foreach (var f in cv.Fields) Rebind(f.Value, schema);
                    break;
                case DictValue dict:
                    foreach (var item in dict.Items) Rebind(item.Value, schema);
                    break;
                case ListValue list:
                    foreach (var item in list.Items) Rebind(item, schema);
                    break;
            }
        }

        private static int CountSurvivors(List<ModUnit> units)
        {
            int n = 0;
            foreach (var u in units)
                if (!u.Failed) n++;
            return n;
        }

        /// <summary>
        /// 살아남은 모드로 스키마를 만든다. 오류가 난 모드는 실패시키고 의존 모드도 빼고 다시 만든다.
        /// 실패한 모드의 진단만 확정하고, 살아남은 모드의 진단은 마지막 회차의 것을 쓴다.
        /// </summary>
        private static SchemaSet BuildSchema(List<ModUnit> units, ModVisibility visibility, DiagnosticBag diagnostics)
        {
            while (true)
            {
                var stage = new StageDiagnostics(SurvivorIds(units));
                var mods = new List<ModSchema>();
                foreach (var u in units)
                    if (!u.Failed) mods.Add(u.Schema);
                var schema = SchemaBuilder.Build(mods, visibility, stage);
                if (!Settle(units, stage, diagnostics)) return schema;
            }
        }

        private static List<string> SurvivorIds(List<ModUnit> units)
        {
            var ids = new List<string>();
            foreach (var u in units)
                if (!u.Failed) ids.Add(u.Manifest.Id);
            return ids;
        }

        /// <summary>Def를 로드 순서대로 등록한다. 고정점은 <see cref="BuildSchema"/>와 같다.</summary>
        private static DefRegistry BuildDefs(List<ModUnit> units, ModVisibility visibility, LoadOrder order,
            DiagnosticBag diagnostics)
        {
            while (true)
            {
                var stage = new StageDiagnostics(SurvivorIds(units));
                var registry = new DefRegistry();
                foreach (var u in units)
                {
                    if (u.Failed) continue;
                    foreach (var record in u.Defs) Register(registry, record, visibility, order, stage);
                }
                if (!Settle(units, stage, diagnostics)) return registry;
            }
        }

        /// <summary>
        /// 회차 결과를 반영한다. 자기 오류가 있는 모드를 먼저 실패시키고, 그런 모드가 없을 때만 충돌 오류가 있는 모드를
        /// 실패시킨다 (spec-gaps G28). 실패시킨 모드가 있으면 그 진단을 확정하고 의존 모드도 실패시킨 뒤 true.
        /// 없으면 살아남은 모드의 진단(경고 등)을 확정하고 false.
        /// </summary>
        private static bool Settle(List<ModUnit> units, StageDiagnostics stage, DiagnosticBag diagnostics)
        {
            bool anyFailed = false;
            foreach (bool conflictsOnly in new[] { false, true })
            {
                foreach (var u in units)
                {
                    if (u.Failed) continue;
                    string id = u.Manifest.Id;
                    if (!(conflictsOnly ? stage.HasConflictErrors(id) : stage.HasOwnErrors(id))) continue;
                    u.Failed = true;
                    anyFailed = true;
                    u.Bag.AddRange(stage.All(id));
                }
                if (anyFailed) break;
            }
            if (!anyFailed)
            {
                foreach (var u in units)
                    if (!u.Failed) u.Bag.AddRange(stage.All(u.Manifest.Id));
                return false;
            }
            foreach (var u in units) FailDependents(u, units, diagnostics);
            return true;
        }

        /// <summary>
        /// Def 하나를 등록한다. 중복 id는 오류이고, 의도적 교체는 <c>override=True</c> (spec S1).
        /// Def id는 타입과 무관하게 전역에서 하나다 (spec-gaps G19).
        /// </summary>
        private static void Register(DefRegistry registry, DefRecord record, ModVisibility visibility, LoadOrder order,
            StageDiagnostics stage)
        {
            void Error(DefRecord at, string code, string message, string? hint = null, IReadOnlyList<SourceSpan>? related = null) =>
                stage.Error(at.Mod, code, message, at.Span, hint, related);
            void ConflictError(DefRecord at, string code, string message, string? hint = null,
                IReadOnlyList<SourceSpan>? related = null) =>
                stage.ConflictError(at.Mod, code, message, at.Span, hint, related);

            if (!registry.TryGet(record.Id, out var existing))
            {
                if (record.IsOverride)
                {
                    Error(record, Codes.DefBadOverride, "override=True but there is no Def '" + record.Id + "' to replace",
                        hint: Suggest.Hint(record.Id, registry.Ids) ?? "remove override=True to define a new Def");
                    return;
                }
                registry.Add(record);
                return;
            }

            var at = new[] { existing.Span };

            // 이 id의 원래 정의. 이미 교체되었어도 판정은 원래 주인을 기준으로 한다
            var original = existing;
            while (original.Replaced != null) original = original.Replaced;
            bool ownDef = original.Mod == record.Mod || existing.Mod == record.Mod;

            if (!record.IsOverride)
            {
                bool canReplace = !ownDef && visibility.CanSee(record.Mod, original.Mod);
                string message = "Def id '" + record.Id + "' is already defined" +
                                 (ownDef ? "" : " by mod '" + original.Mod + "'");
                if (ownDef || canReplace)
                {
                    Error(record, Codes.DefDuplicateId, message, hint: canReplace ? "add override=True to replace it" : null,
                        related: new[] { original.Span });
                    return;
                }
                // 서로 의존하지 않는 두 모드: 어느 쪽이 맞는지 근거가 없으므로 둘 다 오류
                ConflictError(record, Codes.DefDuplicateId, message, related: new[] { original.Span });
                ConflictError(original, Codes.DefDuplicateId,
                    "Def id '" + record.Id + "' is also defined by mod '" + record.Mod + "'", related: new[] { record.Span });
                return;
            }

            if (ownDef)
            {
                Error(record, Codes.DefBadOverride, "a mod cannot override its own Def '" + record.Id + "'", related: at);
                return;
            }
            if (!visibility.CanSee(record.Mod, original.Mod))
            {
                Error(record, Codes.SchNotVisible,
                    "Def '" + record.Id + "' belongs to mod '" + original.Mod + "', which mod '" + record.Mod +
                    "' does not depend on", hint: "add \"" + original.Mod + "\" to dependencies in mod.py", related: at);
                return;
            }
            if (existing.Type != record.Type)
            {
                Error(record, Codes.DefBadOverride,
                    "an override must keep the type: '" + record.Id + "' is a " + existing.Type.Name + ", not a " +
                    record.Type.Name, related: at);
                return;
            }
            // 두 모드가 같은 Def를 교체: 순서 관계가 선언되어 있으면 나중이 이기고, 아니면 둘 다 오류 (set 충돌과 같은 규칙)
            if (existing.IsOverride && existing.Mod != record.Mod && !order.IsOrdered(existing.Mod, record.Mod))
            {
                string message = "Def '" + record.Id + "' is overridden by both mod '" + existing.Mod + "' and mod '" +
                                 record.Mod + "', which have no load order between them";
                ConflictError(record, Codes.DefBadOverride, message, hint: "declare loadAfter in one of them", related: at);
                ConflictError(existing, Codes.DefBadOverride, message, hint: "declare loadAfter in one of them",
                    related: new[] { record.Span });
                return;
            }
            registry.Replace(record);
        }
    }
}
