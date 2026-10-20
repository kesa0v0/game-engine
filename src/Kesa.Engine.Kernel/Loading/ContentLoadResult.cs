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
        /// 보기 정책 함수 <c>may_see(viewer, entity, component)</c> (spec-gaps G196·G197·G203·G238). 활성 모드 전체에서 최대 하나이고
        /// 교체됐으면 마지막 것이다. 없으면 null이고 그때 거른 읽기는 모두 보인다.
        /// </summary>
        public PolicyDecl? ViewPolicy { get; internal set; }

        /// <summary>
        /// ①의 정렬 결과. 두 모드 사이에 선언된 순서가 있는지(<see cref="LoadOrder.IsOrdered"/>)를 번역의 같은 키 경고가 쓴다 (spec-gaps G256).
        /// </summary>
        public LoadOrder? Order { get; internal set; }

        /// <summary>모드들이 있는 폴더. <see cref="ModManifest.Directory"/>는 이 기준이다. 번역 파일(<c>locale/</c>)을 찾는 데 쓴다 (G256).</summary>
        public string ModsRoot { get; internal set; } = "";

        /// <summary>살아남은 모드별 함수 본문의 <c>text("키", ...)</c> 리터럴 호출 (모드 id → 호출, G257). <c>check</c>의 번역 검사가 본다.</summary>
        public IReadOnlyDictionary<string, IReadOnlyList<TextUse>> TextUses { get; internal set; } =
            new SortedDictionary<string, IReadOnlyList<TextUse>>(StringComparer.Ordinal);

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
}
