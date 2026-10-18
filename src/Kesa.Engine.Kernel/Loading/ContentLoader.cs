using System;
using System.Collections.Generic;
using System.IO;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Schema;
using Kesa.Engine.Host;
using Kesa.Engine.Script;

namespace Kesa.Engine.Loading
{
    /// <summary>②의 결과: 완성된 스키마와 등록된 Def.</summary>
    public sealed class ContentLoadResult
    {
        internal ContentLoadResult(IReadOnlyList<ModManifest> mods, IReadOnlyList<string> disabled, SchemaSet schema,
            DefRegistry defs, IReadOnlyList<PatchCall> patches, IReadOnlyList<ResolvedPatch> resolved, PatchLog log,
            ResolvedDefs expanded, DefSet frozen, DefinitionSnapshot firstRound,
            IReadOnlyDictionary<string, ModScope> scopes, IReadOnlyDictionary<string, CommandDecl> commands)
        {
            Scopes = scopes;
            Commands = commands;
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

        /// <summary>살아남은 모드가 선언한 커맨드 (id <c>모드id.함수이름</c> → 선언, id 서수 순, spec-gaps G109).</summary>
        public IReadOnlyDictionary<string, CommandDecl> Commands { get; }

        /// <summary>
        /// 제출 정책 함수 (spec-gaps G122·G124). 활성 모드 전체에서 최대 하나이고 교체됐으면 마지막 것이다. 없으면 null이고
        /// 그때 입구는 모든 issuer를 허용한다.
        /// </summary>
        public PolicyDecl? SubmitPolicy { get; internal set; }

        /// <summary>
        /// 보기 정책 함수 <c>may_see(issuer, entity, component)</c> (spec-gaps G196·G197·G203). 활성 모드 전체에서 최대 하나이고
        /// 교체됐으면 마지막 것이다. 없으면 null이고 그때 거른 읽기는 모두 보인다.
        /// </summary>
        public PolicyDecl? ViewPolicy { get; internal set; }

        /// <summary>살아남은 모드의 <c>@on</c> 핸들러, 모드 로드 순서 → 정의 순서 (spec S5 동률 규칙, spec-gaps G145).</summary>
        public IReadOnlyList<HandlerDecl> Handlers { get; internal set; } = Array.Empty<HandlerDecl>();

        /// <summary>살아남은 모드의 <c>@when</c> 반응, 모드 로드 순서 → 정의 순서 (spec S5, spec-gaps G169).</summary>
        public IReadOnlyList<WhenDecl> Whens { get; internal set; } = Array.Empty<WhenDecl>();

        /// <summary>
        /// 살아남은 모드의 내용 지문 (모드 id → 지문, spec S8, spec-gaps G212). 엔진이 읽은 파일별 지문과 전체 지문이다.
        /// 무엇이 달라도 괜찮은지는 사용처가 이것으로 정한다.
        /// </summary>
        public IReadOnlyDictionary<string, ModContent> Contents { get; internal set; } =
            new SortedDictionary<string, ModContent>(StringComparer.Ordinal);

        /// <summary>
        /// 소유 모드가 살아남았고 서명이 풀린 호스트 함수 (전체 이름 → 서명, spec S7, spec-gaps G215·G218).
        /// 소유 모드가 없는 등록은 여기 없다.
        /// </summary>
        public IReadOnlyDictionary<string, HostSignature> HostFunctions { get; internal set; } =
            new SortedDictionary<string, HostSignature>(StringComparer.Ordinal);
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
    public static partial class ContentLoader
    {
        /// <summary>이 폴더 아래 파일은 스키마 파일이다 (spec-gaps G16).</summary>
        public const string SchemaFolder = "schema/";

        public static ContentLoadResult Load(string modsRoot, LoadOrder order, DiagnosticBag diagnostics,
            HostFunctions? host = null)
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
                var run = RunDefinitionStages(units, order, runDiagnostics, host);
                // ③ 이후에 빠질 모드도 아직 들어 있을 때 남긴다 (G83)
                firstRound ??= new DefinitionSnapshot(SurvivingManifests(units), run.Schema, run.Defs);
                if (ExcludeFailedAtPatchStage(units, run, order, runDiagnostics)) continue;

                diagnostics.AddRange(runDiagnostics.ToSortedList());
                var mods = new List<ModManifest>();
                var patches = new List<PatchCall>();
                var scopes = new SortedDictionary<string, ModScope>(StringComparer.Ordinal);
                var commands = new SortedDictionary<string, CommandDecl>(StringComparer.Ordinal);
                var handlers = new List<HandlerDecl>();
                var whens = new List<WhenDecl>();
                var contents = new SortedDictionary<string, ModContent>(StringComparer.Ordinal);
                var hostFunctions = new SortedDictionary<string, HostSignature>(StringComparer.Ordinal);
                foreach (var u in units)
                {
                    diagnostics.AddRange(u.Bag.ToSortedList());
                    if (u.Failed)
                    {
                        disabled.Add(u.Manifest.Id);
                        continue;
                    }
                    mods.Add(u.Manifest);
                    var files = new List<ModFile>(u.Files);
                    files.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));
                    contents.Add(u.Manifest.Id, new ModContent(u.Manifest.Id, files));
                    foreach (var signature in u.Hosts) hostFunctions.Add(signature.Function.Name, signature);
                    patches.AddRange(u.Patches);
                    scopes.Add(u.Manifest.Id, u.Scope);
                    // 같은 모드 안의 같은 이름은 EVL004로 막히고 모드 id는 유일하므로 id는 겹치지 않는다
                    foreach (var command in u.Functions.Commands) commands.Add(command.Id, command);
                    handlers.AddRange(u.Functions.Handlers);
                    whens.AddRange(u.Functions.Whens);
                }
                return new ContentLoadResult(mods, new List<string>(disabled), run.Schema, run.Defs, patches, run.Resolved,
                    run.Log, run.Expanded, DefSet.Freeze(run.Expanded), firstRound, scopes, commands)
                {
                    SubmitPolicy = run.Policy,
                    ViewPolicy = run.ViewPolicy,
                    Handlers = handlers,
                    Whens = whens,
                    Contents = contents,
                    HostFunctions = hostFunctions,
                };
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

            /// <summary>제출 정책 함수 (G124). 없으면 null.</summary>
            public PolicyDecl? Policy { get; set; }

            /// <summary>보기 정책 함수 (G203). 없으면 null.</summary>
            public PolicyDecl? ViewPolicy { get; set; }

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
        private static DefinitionRun RunDefinitionStages(List<ModUnit> units, LoadOrder order, DiagnosticBag runDiagnostics,
            HostFunctions? host)
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
                var hostNames = HostNamesFor(u, units, visibility, host);
                CheckHostShadowing(u, hostNames);
                CheckFunctionNames(u, builtins, hostNames);
                FunctionEffects.Check(u.Functions, u.Scope, schema, new CallContext(u.Manifest.Id, u.Bag), hostNames);
                CommandSignatures.Resolve(u.Functions.Commands, schema, visibility, u.Scope,
                    new CallContext(u.Manifest.Id, u.Bag));
                HandlerStages.Check(u.Functions.Handlers, schema, visibility, new CallContext(u.Manifest.Id, u.Bag));
                CommandSignatures.ResolveWhens(u.Functions.Whens, schema, visibility, u.Scope, new CallContext(u.Manifest.Id, u.Bag));
                HandlerStages.Check(u.Functions.Whens, schema, visibility, new CallContext(u.Manifest.Id, u.Bag));
                if (host != null)
                    u.Hosts = HostSignatures.Resolve(host.Of(u.Manifest.Id), schema, visibility, new CallContext(u.Manifest.Id, u.Bag));
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
            var defs = BuildDefs(units, visibility, order, runDiagnostics, out var policy, out var viewPolicy);
            if (CountSurvivors(units) != survivorsBefore)
            {
                schema = BuildSchema(units, visibility, runDiagnostics);
                Rebind(defs, schema);
            }
            return new DefinitionRun(schema, defs, visibility) { Policy = policy, ViewPolicy = viewPolicy };
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
            var commands = new List<CommandDecl>();
            var functions = new List<KeyValuePair<string, ModFunctions>>();
            foreach (var u in units)
            {
                if (u.Failed) continue;
                commands.AddRange(u.Functions.Commands);
                functions.Add(new KeyValuePair<string, ModFunctions>(u.Manifest.Id, u.Functions));
            }
            Linker.Check(run.Expanded, run.Defs, run.Schema, run.Visibility, id => units.Find(u => u.Manifest.Id == id)?.Manifest, stage,
                commands, functions);
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
    }
}
