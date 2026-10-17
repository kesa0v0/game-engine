namespace Kesa.Engine.Diagnostics
{
    /// <summary>
    /// 진단 코드. 잠정 체계: 영역 접두사 + 세 자리 번호 (spec-gaps G6).
    /// LEX = 렉서, PAR = 파서, MOD = 모드 매니페스트·정렬, LOD = 콘텐츠 파일 목록, EVL = 로드 시점 평가,
    /// SCH = 스키마 단계(②a), DEF = Def 등록(②b), PAT = 패치(③), INH = 상속 펼치기(④), LNK = 링크(⑤), VAL = 검증(⑥),
    /// RUN = 런타임 월드, CMD = 커맨드 선언과 함수 효과(쓰기), SAV = 저장과 불러오기, HST = 호스트 함수.
    /// </summary>
    public static class Codes
    {
        public const string LexTab = "LEX001";
        public const string LexBadIndent = "LEX002";
        public const string LexUnterminatedString = "LEX003";
        public const string LexBadEscape = "LEX004";
        public const string LexUnsupportedString = "LEX005";
        public const string LexBadNumber = "LEX006";
        public const string LexFixedPrecision = "LEX007";
        public const string LexIntOverflow = "LEX008";
        public const string LexUnexpectedChar = "LEX009";
        public const string LexUnbalanced = "LEX010";
        public const string LexNonAsciiIdentifier = "LEX011";

        public const string ParSyntax = "PAR001";
        public const string ParUnsupported = "PAR002";
        public const string ParDuplicateKeyword = "PAR003";
        public const string ParPositionalAfterKeyword = "PAR004";

        public const string ModNoManifest = "MOD001";
        public const string ModBadManifest = "MOD002";
        public const string ModUnknownField = "MOD003";
        public const string ModNotLiteral = "MOD004";
        public const string ModBadId = "MOD005";
        public const string ModDuplicateId = "MOD006";
        public const string ModMissingDependency = "MOD007";
        public const string ModCycle = "MOD008";
        public const string ModDependencyFailed = "MOD009";
        public const string ModSelfReference = "MOD010";

        // 콘텐츠 파일 실행 (spec S1 ②)
        public const string LodBadUse = "LOD001";

        public const string EvlUnknownName = "EVL001";
        public const string EvlUnsupported = "EVL002";
        public const string EvlArguments = "EVL003";
        public const string EvlNameTaken = "EVL004";
        public const string EvlType = "EVL005";
        public const string EvlBadCondition = "EVL006";
        public const string EvlGuardBody = "EVL007";
        public const string EvlModNotDeclared = "EVL008";

        public const string SchDuplicateName = "SCH001";
        public const string SchUnknownName = "SCH002";
        public const string SchNotVisible = "SCH003";
        public const string SchBadDeclaration = "SCH004";
        public const string SchMemberConflict = "SCH005";
        public const string SchDefInSchemaFile = "SCH006";
        public const string SchSchemaInDefFile = "SCH007";
        public const string SchModLoadedInSchemaFile = "SCH008";

        public const string DefDuplicateId = "DEF001";
        public const string DefUnknownField = "DEF002";
        public const string DefBadValue = "DEF003";
        public const string DefBadOverride = "DEF004";

        // 패치 (spec S1 ③)
        public const string PatBadPath = "PAT001";
        public const string PatSkipped = "PAT002";
        public const string PatTargetNotFound = "PAT003";
        public const string PatTargetNotVisible = "PAT004";
        public const string PatNeedsGuard = "PAT005";
        public const string PatBadField = "PAT006";
        public const string PatBadPathStep = "PAT007";
        public const string PatBadOp = "PAT008";
        public const string PatBadValue = "PAT009";
        public const string PatConflict = "PAT010";
        public const string PatOverridden = "PAT011";

        // 패치 적용 (spec S1 ④, 값에 의존하는 검사)
        public const string PatNoComponent = "PAT012";
        public const string PatAlreadyThere = "PAT013";
        public const string PatNoTarget = "PAT014";
        public const string PatNoBase = "PAT015";
        public const string PatKeyExists = "PAT016";
        public const string PatOutOfRange = "PAT017";

        // 상속 펼치기 (spec S1 ④)
        public const string InhParentNotFound = "INH001";
        public const string InhParentType = "INH002";
        public const string InhCycle = "INH003";
        public const string InhNoParent = "INH004";
        public const string InhNoValue = "INH005";

        // 링크 (spec S1 ⑤)
        public const string LnkNotFound = "LNK001";
        public const string LnkWrongType = "LNK002";
        public const string LnkNotVisible = "LNK003";
        public const string LnkAbstract = "LNK004";

        // 검증 (spec S1 ⑥)
        public const string ValMissing = "VAL001";
        public const string ValType = "VAL002";
        public const string ValRange = "VAL003";

        // 런타임 월드 (spec S4, spec-gaps G86~G93)
        public const string RunUnknownName = "RUN001";
        public const string RunDeadEntity = "RUN002";
        public const string RunNotWritable = "RUN003";
        public const string RunNotComputed = "RUN004";
        public const string RunType = "RUN005";
        public const string RunRange = "RUN006";
        public const string RunMissingValue = "RUN007";
        public const string RunNoComponent = "RUN008";
        public const string RunNoRow = "RUN009";
        public const string RunDeadRef = "RUN010";
        public const string RunNotEntity = "RUN011";

        // 순수 함수 실행 (spec S2·S4, spec-gaps G95~G103)
        public const string RunArithmetic = "RUN012";
        public const string RunBudget = "RUN013";
        public const string RunCycle = "RUN014";
        public const string RunScript = "RUN015";

        // 제출 입구 (spec S6, spec-gaps G124): 호스트가 정책 필수를 켰는데 정책 함수가 없다
        public const string RunNoSubmitPolicy = "RUN016";

        // 거른 읽기 (spec-gaps G203): 호스트가 보기 정책 필수를 켰는데 보기·입구 정책이 없다 / 보기 정책이 실패해 "안 보임"으로 처리함
        public const string RunNoViewPolicy = "RUN017";
        public const string RunViewPolicy = "RUN018";

        // 저장과 불러오기 (spec S8, spec-gaps G140): 깨졌거나 형식이 다른 저장 / 지금 내용과 어긋나는 저장 / 모드 목록이 달라짐(경고)
        public const string SaveCorrupt = "SAV001";
        public const string SaveMismatch = "SAV002";
        public const string SaveModsChanged = "SAV003";

        // 멈춘 Flow의 함수(프레임의 함수, 람다가 든 함수)의 본문이 저장 뒤에 바뀜 (spec S8·C5, spec-gaps G144)
        public const string SaveFunctionChanged = "SAV004";

        // 호스트가 허락해(dropRemoved) 지워진 컴포넌트·멤버·Def 키 줄의 저장된 값을 버리고 불러옴 (경고, spec S8, spec-gaps G210)
        public const string SaveDropped = "SAV005";

        // 호스트가 허락해(cancelBrokenFlows) 지금 내용으로 이어 갈 수 없는 멈춘 Flow를 질문과 함께 취소하고 불러옴 (경고, spec S8, spec-gaps G214)
        public const string SaveFlowCanceled = "SAV006";

        // 호스트 함수의 서명을 지금 스키마로 풀지 못함: 없는 Def 타입, 소유 모드에 안 보이는 타입, 맞지 않는 기본값 (spec S7, spec-gaps G215)
        public const string HostSignature = "HST001";

        // 커맨드 선언 (spec S6, spec-gaps G109)
        public const string CmdBadDecorator = "CMD001";
        public const string CmdPreconditions = "CMD002";
        public const string CmdParameterType = "CMD003";

        // 함수 효과: 쓰기 함수를 부를 수 있는 자리 (spec S2 함수 효과 표, spec-gaps G117)
        public const string CmdWriteEffect = "CMD004";

        // 제출 정책 함수: 모양·개수·교체 (spec S6 입구, spec-gaps G122·G124·G130)
        public const string CmdSubmitPolicy = "CMD005";

        // 보기 정책 함수: 모양·개수·교체 (spec-gaps G197·G203)
        public const string CmdViewPolicy = "CMD006";
    }
}
