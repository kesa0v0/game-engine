namespace Kesa.Engine.Diagnostics
{
    /// <summary>
    /// 진단 코드. 잠정 체계: 영역 접두사 + 세 자리 번호 (spec-gaps G6).
    /// LEX = 렉서, PAR = 파서, MOD = 모드 매니페스트·정렬, LOD = 콘텐츠 파일 목록, EVL = 로드 시점 평가,
    /// SCH = 스키마 단계(②a), DEF = Def 등록(②b), PAT = 패치(③), INH = 상속 펼치기(④), LNK = 링크(⑤), VAL = 검증(⑥),
    /// RUN = 런타임 월드, CMD = 커맨드 선언과 함수 효과(쓰기), SAV = 저장과 불러오기, HST = 호스트 함수, LOC = 번역 파일(커널 밖 Kesa.Engine.Text).
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

        /// <summary>엔진이 넘긴 컴포넌트 값을 문자열 리터럴과 비교함: 늘 거짓 (경고, spec-gaps G308).</summary>
        public const string EvlSchemaNameString = "EVL009";

        public const string SchDuplicateName = "SCH001";
        public const string SchUnknownName = "SCH002";
        public const string SchNotVisible = "SCH003";
        public const string SchBadDeclaration = "SCH004";
        public const string SchMemberConflict = "SCH005";
        public const string SchDefInSchemaFile = "SCH006";
        public const string SchSchemaInDefFile = "SCH007";
        public const string SchModLoadedInSchemaFile = "SCH008";
        public const string SchDerivedCycle = "SCH009";

        public const string DefDuplicateId = "DEF001";
        public const string DefUnknownField = "DEF002";
        public const string DefBadValue = "DEF003";
        public const string DefBadOverride = "DEF004";
        public const string DefBadId = "DEF005";

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
        /// <summary>경고: DefId 값을 맨 문자열 리터럴과 비교하거나 그것으로 찾음 (값은 늘 전체 이름이라 맞지 않음, G261).</summary>
        public const string LnkBareDefId = "LNK005";

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

        // RUN016(입구 정책 필수인데 없음, G124)은 입구 정책이 없어져(G237) 쓰지 않는다. 번호는 다시 쓰지 않는다.

        // 배정 표 (spec-gaps G236): 죽은 엔티티에 앉히기, 빈 연결 꼬리표
        public const string RunSeat = "RUN022";

        // 거른 읽기 (spec-gaps G203): 호스트가 보기 정책 필수를 켰는데 보기 정책이 없다 / 보기 정책이 실패해 "안 보임"으로 처리함
        public const string RunNoViewPolicy = "RUN017";
        public const string RunViewPolicy = "RUN018";

        // 호스트 함수 경계 (spec S7, spec-gaps G219): 구현이 예외를 던짐 / 서명과 다른 값을 돌려줌 (둘 다 호스트 코드의 버그)
        public const string RunHostFailed = "RUN019";
        public const string RunHostReturn = "RUN020";

        // 무거운 행동 (spec-gaps G225, 경고): 행동 실행 하나가 읽기 예산보다 많은 단계를 썼다. 실패하지 않고 결과에 붙는다
        public const string RunHeavy = "RUN021";

        // 호스트가 부르는 모드 함수 (spec-gaps G245·G247): 쓰기·난수 함수나 입구라 부르지 않음 / 인자 수가 다름
        public const string RunNotCallable = "RUN023";

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

        // 호스트 함수 이름의 잘못된 사용: 이름 공간을 값으로 씀, 함수에 속성을 붙임, 모드 최상위 이름이 이름 공간을 가림 (spec S7, spec-gaps G218)
        public const string HostName = "HST002";

        // 번역 파일 (spec X4). 번역 오류는 모드를 끄지 않고 그 글만 뺀다 (spec-gaps G257)
        // .ftl 구문 오류 (G257)
        public const string LocSyntax = "LOC001";
        // 한 모드 안의 같은 언어에서 같은 키 (G256)
        public const string LocDuplicate = "LOC002";
        // 순서 없는 두 모드가 같은 언어에서 같은 키를 정의함, 나중 모드가 이김 (경고, G256)
        public const string LocOverridden = "LOC003";
        // locale/ 아래 폴더 이름이 언어 태그가 아님 (G256)
        public const string LocBadLanguage = "LOC004";
        // locale/이 있는데 defaultLanguage가 없거나 그 언어 폴더가 없음 (G257)
        public const string LocNoDefaultLanguage = "LOC005";
        // 아래는 check의 번역 검사 (G257)
        // 번역문의 JOSA에 모르는 조사 (G268)
        public const string LocUnknownParticle = "LOC006";
        // 엔진 예약 접두사의 키: 불린 Def의 것이 아닌 def- 키, 엔진·호스트가 주지 않은 engine- 키 (G262·G273)
        public const string LocReservedKey = "LOC007";
        // 스크립트의 text("키")가 어느 언어에도 없음 (경고)
        public const string LocMissingKey = "LOC008";
        // 번역문이 쓰는 변수를 그 text() 호출이 넘기지 않음 (경고)
        public const string LocMissingArgument = "LOC009";
        // 다른 언어의 글이 주인의 기본 언어 글에 없는 변수를 씀 (경고)
        public const string LocExtraVariable = "LOC010";
        // 모드·언어마다 빠진 글의 수 (정보)
        public const string LocUntranslated = "LOC011";

        // 커맨드 선언 (spec S6, spec-gaps G109)
        public const string CmdBadDecorator = "CMD001";
        public const string CmdPreconditions = "CMD002";
        public const string CmdParameterType = "CMD003";

        // 함수 효과: 쓰기 함수를 부를 수 있는 자리 (spec S2 함수 효과 표, spec-gaps G117)
        public const string CmdWriteEffect = "CMD004";

        // 없어진 제출 정책 함수 @submit_policy를 선언함 (spec-gaps G237, 옛 G122·G124·G130의 모양·개수·교체)
        public const string CmdSubmitPolicy = "CMD005";

        // 보기 정책 함수: 모양·개수·교체 (spec-gaps G197·G203)
        public const string CmdViewPolicy = "CMD006";

        // UI 로컬 효과의 자리 (spec-gaps G278·G282): 게임 쪽 코드가 ui를 읽거나 쓰고, @ui_action이 게임을 바꿈
        public const string CmdUiEffect = "CMD007";

        // submit("커맨드", ...)의 대상: 없는 커맨드, 보이지 않는 커맨드, 매개변수 이름 (spec-gaps G278)
        public const string CmdSubmit = "CMD008";
    }
}
