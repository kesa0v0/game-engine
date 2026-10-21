using System;
using System.Collections.Generic;
using Kesa.Engine.Diagnostics;

namespace Kesa.Engine.Script
{
    /// <summary>
    /// 모드가 정의한 로직 함수와 최상위에서 묶으려 한 이름 (spec-gaps G108). 인터프리터가 모드의 파일을 실행하며 채우고,
    /// 모드 평가가 끝난 뒤 <see cref="FunctionNameChecker"/>가 본다.
    /// </summary>
    public sealed class ModFunctions
    {
        /// <summary>정의된 순서대로의 <c>def</c>와 최상위에서 만든 람다. 함수 안의 람다는 바깥 함수와 함께 본다.</summary>
        public List<FunctionValue> Functions { get; } = new List<FunctionValue>();

        /// <summary>
        /// 최상위 대입·<c>def</c>가 묶으려 한 이름. 그 문장이 오류로 실패해 이름 공간에 없어도 들어 있어,
        /// 원인 오류 하나가 함수 본문의 "정의되지 않은 이름" 오류로 번지지 않게 한다.
        /// </summary>
        public SortedSet<string> TopLevelNames { get; } = new SortedSet<string>(StringComparer.Ordinal);

        /// <summary><c>@command</c>로 선언한 커맨드, 정의된 순서대로 (spec-gaps G109).</summary>
        public List<CommandDecl> Commands { get; } = new List<CommandDecl>();

        /// <summary><c>@ui_action</c>으로 선언한 UI 동작, 정의된 순서대로 (G280). 커맨드와 다른 목록이다.</summary>
        public List<CommandDecl> UiActions { get; } = new List<CommandDecl>();

        /// <summary>
        /// <c>@view_policy</c>(G197)로 선언한 보기 정책 함수. 모드 안에서 둘 이상이면 로드 오류다.
        /// </summary>
        public List<PolicyDecl> Policies { get; } = new List<PolicyDecl>();

        /// <summary><c>@on(신호)</c>로 붙인 핸들러, 정의된 순서대로 (G145).</summary>
        public List<HandlerDecl> Handlers { get; } = new List<HandlerDecl>();

        /// <summary><c>@when(조건)</c> 반응, 정의된 순서대로 (G169).</summary>
        public List<WhenDecl> Whens { get; } = new List<WhenDecl>();

        /// <summary><c>@wrap(대상)</c>으로 감싸는 함수, 정의된 순서대로 (G311~G314). 대상은 로더가 푼다.</summary>
        public List<WrapDecl> Wraps { get; } = new List<WrapDecl>();

        /// <summary>
        /// 함수 본문에서 내장 <c>spawn("Def id", ...)</c>에 리터럴로 적은 Def id (G134). 이름 검사가 모으고 ⑤ 링크가 본다.
        /// </summary>
        public List<KeyValuePair<string, SourceSpan>> SpawnedDefs { get; } = new List<KeyValuePair<string, SourceSpan>>();

        /// <summary>
        /// <c>defid("Def id")</c>에 적은 Def id (G266): 함수 본문의 리터럴은 이름 검사가, Def 파일 최상위에서 부른 것은 그 내장이 모으고
        /// ⑤ 링크가 본다.
        /// </summary>
        public List<KeyValuePair<string, SourceSpan>> DefIdLiterals { get; } = new List<KeyValuePair<string, SourceSpan>>();

        /// <summary>함수 본문의 <c>text("키", ...)</c> 리터럴 호출 (G257). 이름 검사가 모으고 <c>check</c>의 번역 검사가 본다.</summary>
        public List<TextUse> TextUses { get; } = new List<TextUse>();

        /// <summary>함수 본문의 <c>submit("커맨드", ...)</c> 리터럴 호출 (G278). 이름 검사가 모으고 로더가 대상을 본다.</summary>
        public List<SubmitUse> SubmitUses { get; } = new List<SubmitUse>();

        /// <summary>
        /// 함수 본문의 <c>query(...)</c> 호출마다 리터럴로 적은 출처 이름 (G192). 이름 검사가 모으고 ⑤ 링크가 본다.
        /// </summary>
        public List<List<KeyValuePair<string, SourceSpan>>> QueriedNames { get; } = new List<List<KeyValuePair<string, SourceSpan>>>();

        /// <summary>함수 본문의 <c>defs(City)</c>에 리터럴로 적은 Def 타입 이름 (G305). 이름 검사가 모으고 ⑤ 링크가 본다.</summary>
        public List<KeyValuePair<string, SourceSpan>> DefTypeLiterals { get; } = new List<KeyValuePair<string, SourceSpan>>();
    }
}
