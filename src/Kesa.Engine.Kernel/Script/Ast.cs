using System.Collections.Generic;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Numerics;

namespace Kesa.Engine.Script
{
    public abstract class Node
    {
        protected Node(SourceSpan span) => Span = span;

        public SourceSpan Span { get; }
    }

    // ---- 식 ----

    public abstract class Expr : Node
    {
        protected Expr(SourceSpan span) : base(span) { }

        /// <summary>
        /// 이 식을 칸 배치 하나에서 풀어 만든 실행 노드 (G226). 실행기만 채우고 읽으며, 쓸 때마다 배치가 처음과 같은 객체인지 확인한다.
        /// </summary>
        internal object? Compiled { get; set; }
    }

    public sealed class NameExpr : Expr
    {
        public NameExpr(string name, SourceSpan span) : base(span) => Name = name;
        public string Name { get; }

        /// <summary>
        /// 이 자리에서 이름이 전역·내장으로 풀린 결과 (인라인 캐시). <see cref="AttributeExpr.Cache"/>처럼 실행만 채우고 읽으며, 쓸 때마다
        /// 같은 실행기·같은 모드이고 그 이름이 지역 변수가 아닌지 확인한다.
        /// </summary>
        internal object? Cache { get; set; }
    }

    public sealed class IntLiteral : Expr
    {
        public IntLiteral(long value, SourceSpan span) : base(span)
        {
            Value = value;
            Constant = new IntValue(value);
        }
        public long Value { get; }

        /// <summary>평가 값. 값은 불변이라(G104) 평가할 때마다 새로 만들지 않고 이것을 돌려준다.</summary>
        internal IntValue Constant { get; }
    }

    public sealed class FixedLiteral : Expr
    {
        public FixedLiteral(Fixed value, SourceSpan span) : base(span)
        {
            Value = value;
            Constant = new FixedValue(value);
        }
        public Fixed Value { get; }

        /// <inheritdoc cref="IntLiteral.Constant"/>
        internal FixedValue Constant { get; }
    }

    public sealed class StringLiteral : Expr
    {
        public StringLiteral(string value, SourceSpan span) : base(span)
        {
            Value = value;
            Constant = new StrValue(value);
        }
        public string Value { get; }

        /// <inheritdoc cref="IntLiteral.Constant"/>
        internal StrValue Constant { get; }
    }

    public sealed class BoolLiteral : Expr
    {
        public BoolLiteral(bool value, SourceSpan span) : base(span) => Value = value;
        public bool Value { get; }
    }

    public sealed class NoneLiteral : Expr
    {
        public NoneLiteral(SourceSpan span) : base(span) { }
    }

    public sealed class ListExpr : Expr
    {
        public ListExpr(IReadOnlyList<Expr> items, SourceSpan span) : base(span) => Items = items;
        public IReadOnlyList<Expr> Items { get; }
    }

    public sealed class DictEntry
    {
        public DictEntry(Expr key, Expr value)
        {
            Key = key;
            Value = value;
        }

        public Expr Key { get; }
        public Expr Value { get; }
    }

    public sealed class DictExpr : Expr
    {
        public DictExpr(IReadOnlyList<DictEntry> entries, SourceSpan span) : base(span) => Entries = entries;
        public IReadOnlyList<DictEntry> Entries { get; }
    }

    public sealed class Argument
    {
        public Argument(string? name, Expr value, SourceSpan span)
        {
            Name = name;
            Value = value;
            Span = span;
        }

        /// <summary>키워드 인자면 이름, 위치 인자면 null.</summary>
        public string? Name { get; }
        public Expr Value { get; }
        public SourceSpan Span { get; }
    }

    public sealed class CallExpr : Expr
    {
        public CallExpr(Expr callee, IReadOnlyList<Argument> arguments, SourceSpan span) : base(span)
        {
            Callee = callee;
            Arguments = arguments;
        }

        public Expr Callee { get; }
        public IReadOnlyList<Argument> Arguments { get; }
    }

    public sealed class AttributeExpr : Expr
    {
        public AttributeExpr(Expr target, string name, SourceSpan span) : base(span)
        {
            Target = target;
            Name = name;
        }

        public Expr Target { get; }
        public string Name { get; }

        /// <summary>
        /// 이 자리에서 이름을 처음 푼 결과 (인라인 캐시, <see cref="ISiteAttributes"/>). 실행이 채우고 실행만 읽는다.
        /// 타입을 로드 때 알 수 없는 <c>u.Food</c>도 같은 자리는 대개 같은 것을 가리키므로 두 번째부터 문자열로 찾지 않는다.
        /// 들어 있는 것은 바뀌지 않는 객체이고 맞는지 확인하고 쓰므로, 틀린 것이 들어 있어도 결과는 캐시가 없을 때와 같다.
        /// </summary>
        internal object? Cache { get; set; }
    }

    public sealed class IndexExpr : Expr
    {
        public IndexExpr(Expr target, IReadOnlyList<Expr> indices, SourceSpan span) : base(span)
        {
            Target = target;
            Indices = indices;
        }

        public Expr Target { get; }

        /// <summary>첫 번째 인덱스.</summary>
        public Expr Index => Indices[0];

        /// <summary>
        /// 쉼표로 나뉜 인덱스들 (<c>dict[K, V]</c>). 파이썬에서는 튜플 하나지만 튜플 값은 없으므로,
        /// 둘 이상은 타입 표기에서만 받고 값 인덱싱에서는 평가기가 거부한다.
        /// </summary>
        public IReadOnlyList<Expr> Indices { get; }
    }

    public sealed class UnaryExpr : Expr
    {
        public UnaryExpr(string op, Expr operand, SourceSpan span) : base(span)
        {
            Op = op;
            Operand = operand;
            Operator = Operators.Unary(op);
        }

        /// <summary>"-", "+", "~", "not"</summary>
        public string Op { get; }
        public Expr Operand { get; }

        /// <summary><see cref="Op"/>의 번호 (G226 ①).</summary>
        internal UnaryOp Operator { get; }
    }

    public sealed class BinaryExpr : Expr
    {
        public BinaryExpr(string op, Expr left, Expr right, SourceSpan span) : base(span)
        {
            Op = op;
            Left = left;
            Right = right;
            Operator = Operators.Binary(op);
        }

        /// <summary>산술·비트·비교·논리 연산자. "not in", "is not"은 공백 하나로 잇는다.</summary>
        public string Op { get; }
        public Expr Left { get; }
        public Expr Right { get; }

        /// <summary><see cref="Op"/>의 번호 (G226 ①).</summary>
        internal BinaryOp Operator { get; }
    }

    /// <summary><c>참일때 if 조건 else 거짓일때</c>.</summary>
    public sealed class ConditionalExpr : Expr
    {
        public ConditionalExpr(Expr condition, Expr whenTrue, Expr whenFalse, SourceSpan span) : base(span)
        {
            Condition = condition;
            WhenTrue = whenTrue;
            WhenFalse = whenFalse;
        }

        public Expr Condition { get; }
        public Expr WhenTrue { get; }
        public Expr WhenFalse { get; }
    }

    public enum ComprehensionKind
    {
        /// <summary><c>[x for ...]</c></summary>
        List,

        /// <summary><c>{k: v for ...}</c></summary>
        Dict,

        /// <summary><c>f(x for ...)</c>: 호출의 인자로만 온다 (spec-gaps G189).</summary>
        Generator,
    }

    /// <summary><c>for 변수 in 반복할것 if 조건 if 조건...</c> 절 하나.</summary>
    public sealed class ComprehensionClause
    {
        public ComprehensionClause(string variable, SourceSpan variableSpan, Expr iterable, IReadOnlyList<Expr> conditions)
        {
            Variable = variable;
            VariableSpan = variableSpan;
            Iterable = iterable;
            Conditions = conditions;
        }

        public string Variable { get; }
        public SourceSpan VariableSpan { get; }
        public Expr Iterable { get; }
        public IReadOnlyList<Expr> Conditions { get; }
    }

    /// <summary>
    /// 컴프리헨션과 제너레이터 식 (spec S2, spec-gaps G189). 절은 바깥에서 안쪽 순이다. 반복 변수는 이 식 안에서만 보인다(파이썬 3).
    /// </summary>
    public sealed class ComprehensionExpr : Expr
    {
        public ComprehensionExpr(ComprehensionKind kind, Expr element, Expr? value, IReadOnlyList<ComprehensionClause> clauses,
            SourceSpan span) : base(span)
        {
            Kind = kind;
            Element = element;
            Value = value;
            Clauses = clauses;
        }

        public ComprehensionKind Kind { get; }

        /// <summary>리스트·제너레이터면 원소, 딕트면 키.</summary>
        public Expr Element { get; }

        /// <summary>딕트의 값.</summary>
        public Expr? Value { get; }

        public IReadOnlyList<ComprehensionClause> Clauses { get; }

        /// <summary>
        /// 식 안의 모든 하위 식과 그 식에서 보이는 반복 변수 (정적 검사기들이 같은 규칙으로 돌도록). 첫 절의 반복할 것은 파이썬처럼
        /// 바깥 이름 공간에서 계산하므로 반복 변수가 보이지 않는다.
        /// </summary>
        public IEnumerable<(Expr Expr, IReadOnlyList<string> Bound)> Parts()
        {
            var bound = new List<string>();
            foreach (var clause in Clauses)
            {
                yield return (clause.Iterable, bound.ToArray());
                bound.Add(clause.Variable);
                foreach (var condition in clause.Conditions) yield return (condition, bound.ToArray());
            }
            yield return (Element, bound.ToArray());
            if (Value != null) yield return (Value, bound.ToArray());
        }
    }

    /// <summary>파싱은 되었지만 이 단계에서 지원하지 않는 식 (진단은 이미 보고됨).</summary>
    public sealed class UnsupportedExpr : Expr
    {
        public UnsupportedExpr(string what, SourceSpan span) : base(span) => What = what;
        public string What { get; }
    }

    // ---- 문장 ----

    public abstract class Stmt : Node
    {
        protected Stmt(SourceSpan span) : base(span) { }

        /// <summary>
        /// 이 문장을 칸 배치 하나에서 풀어 만든 실행 노드 (G226). 실행기만 채우고 읽으며, 쓸 때마다 배치가 처음과 같은 객체인지 확인한다.
        /// </summary>
        internal object? Compiled { get; set; }
    }

    public sealed class ExprStmt : Stmt
    {
        public ExprStmt(Expr expr) : base(expr.Span) => Expr = expr;
        public Expr Expr { get; }
    }

    public sealed class AssignStmt : Stmt
    {
        public AssignStmt(Expr target, string op, Expr value, SourceSpan span) : base(span)
        {
            Target = target;
            Op = op;
            Value = value;
            Compound = Operators.Compound(op);
        }

        public Expr Target { get; }

        /// <summary>"=" 또는 복합 대입 연산자 ("+=" 등).</summary>
        public string Op { get; }
        public Expr Value { get; }

        /// <summary>복합 대입의 연산자 번호 (<c>+=</c>면 더하기). <c>=</c>면 <see cref="BinaryOp.Unsupported"/>다 (G226 ①).</summary>
        internal BinaryOp Compound { get; }
    }

    public sealed class Parameter
    {
        public Parameter(string name, Expr? annotation, SourceSpan span, Expr? @default = null)
        {
            Name = name;
            Annotation = annotation;
            Span = span;
            Default = @default;
        }

        public string Name { get; }
        public Expr? Annotation { get; }
        public SourceSpan Span { get; }

        /// <summary><c>= 값</c>. <c>@command</c> 함수에만 있다 (spec-gaps G116).</summary>
        public Expr? Default { get; }
    }

    /// <summary>
    /// <c>lambda params: body</c>. 본문은 식 하나라 끝까지 파싱한다. 로딩 단계는 <c>def</c>처럼 값으로 묶기만 하고
    /// 호출하지 않는다 (spec-gaps G33).
    /// </summary>
    public sealed class LambdaExpr : Expr
    {
        public LambdaExpr(IReadOnlyList<Parameter> parameters, Expr body, SourceSpan span) : base(span)
        {
            Parameters = parameters;
            Body = body;
        }

        public IReadOnlyList<Parameter> Parameters { get; }
        public Expr Body { get; }

        /// <summary>이 함수의 지역 변수 칸 배치 (실행기가 처음 부를 때 채운다, G226).</summary>
        internal object? Layout { get; set; }
    }

    /// <summary>
    /// <c>def name(params) -> ret:</c>와 본문. 로딩 단계는 함수를 값으로 묶기만 하고 호출하지 않으며,
    /// 본문은 순수 함수 실행기가 실행한다 (spec-gaps G17을 G102가 대체).
    /// </summary>
    public sealed class FunctionDef : Stmt
    {
        public FunctionDef(string name, IReadOnlyList<Parameter> parameters, Expr? returns,
            IReadOnlyList<Stmt> body, SourceSpan span, IReadOnlyList<Expr>? decorators = null) : base(span)
        {
            Name = name;
            Parameters = parameters;
            Returns = returns;
            Body = body;
            Decorators = decorators ?? new List<Expr>();
        }

        /// <summary>
        /// <c>@</c> 뒤의 식들 (위에서 아래 순서). 파서는 모양만 읽고, 무엇이 허용되는지는 인터프리터가 본다
        /// (<c>@command</c>, spec-gaps G109).
        /// </summary>
        public IReadOnlyList<Expr> Decorators { get; }

        /// <summary>첫 데코레이터의 이름 (<c>@command</c>·<c>@command(...)</c> → <c>command</c>). 없거나 이름 모양이 아니면 null.</summary>
        public string? DecoratorName
        {
            get
            {
                if (Decorators.Count == 0) return null;
                var head = Decorators[0] is CallExpr call ? call.Callee : Decorators[0];
                return head is NameExpr name ? name.Name : null;
            }
        }

        /// <summary>
        /// 커맨드로 볼 함수인가: 데코레이터가 있고 <c>@view_policy</c>나 없어진 <c>@submit_policy</c>(G237)가 아니다. 잘못된 데코레이터도 커맨드로 봐서
        /// 원인 오류 하나가 다른 오류로 번지지 않게 한다 (데코레이터 자체는 인터프리터가 검사).
        /// </summary>
        public bool IsCommand => Decorators.Count > 0 && DecoratorName != Interpreter.SubmitPolicyName &&
                                 DecoratorName != Interpreter.ViewPolicyName && !IsHandler && !IsWhen;

        /// <summary><c>@on(신호)</c> 핸들러인가 (spec S5, spec-gaps G145). 본문은 커맨드처럼 Flow지만 사전 조건이 없다.</summary>
        public bool IsHandler => Decorators.Count > 0 && DecoratorName == Interpreter.OnName;

        /// <summary><c>@when(조건)</c> 반응인가 (spec S5, spec-gaps G169). 본문은 핸들러처럼 Flow이고 사전 조건이 없다.</summary>
        public bool IsWhen => Decorators.Count > 0 && DecoratorName == Interpreter.WhenName;

        public string Name { get; }
        public IReadOnlyList<Parameter> Parameters { get; }
        public Expr? Returns { get; }
        public IReadOnlyList<Stmt> Body { get; }

        /// <summary>이 함수의 지역 변수 칸 배치 (실행기가 처음 부를 때 채운다, G226).</summary>
        internal object? Layout { get; set; }
    }

    /// <summary>
    /// <c>with 식:</c> 블록. 쓰이는 곳은 커맨드 본문 첫 문장의 <c>with preconditions:</c> 하나다 (spec S2, spec-gaps G111).
    /// <c>as</c>와 여러 항목은 미지원이다.
    /// </summary>
    public sealed class WithStmt : Stmt
    {
        public WithStmt(Expr context, IReadOnlyList<Stmt> body, SourceSpan span) : base(span)
        {
            Context = context;
            Body = body;
        }

        public Expr Context { get; }
        public IReadOnlyList<Stmt> Body { get; }
    }

    /// <summary><c>return</c> 또는 <c>return 식</c> (함수 본문에서만).</summary>
    public sealed class ReturnStmt : Stmt
    {
        public ReturnStmt(Expr? value, SourceSpan span) : base(span) => Value = value;
        public Expr? Value { get; }
    }

    /// <summary><c>for 이름 in 식:</c>. 대상은 이름 하나다 (풀어 받기와 <c>else</c> 절은 아직 없음, spec-gaps G102).</summary>
    public sealed class ForStmt : Stmt
    {
        public ForStmt(NameExpr target, Expr iterable, IReadOnlyList<Stmt> body, SourceSpan span) : base(span)
        {
            Target = target;
            Iterable = iterable;
            Body = body;
        }

        public NameExpr Target { get; }
        public Expr Iterable { get; }
        public IReadOnlyList<Stmt> Body { get; }
    }

    /// <summary><c>while 식:</c>. 따로 상한은 없고 단계 예산이 막는다 (<c>else</c> 절은 아직 없음, spec-gaps G195).</summary>
    public sealed class WhileStmt : Stmt
    {
        public WhileStmt(Expr condition, IReadOnlyList<Stmt> body, SourceSpan span) : base(span)
        {
            Condition = condition;
            Body = body;
        }

        public Expr Condition { get; }
        public IReadOnlyList<Stmt> Body { get; }
    }

    public sealed class PassStmt : Stmt
    {
        public PassStmt(SourceSpan span) : base(span) { }
    }

    public sealed class BreakStmt : Stmt
    {
        public BreakStmt(SourceSpan span) : base(span) { }
    }

    public sealed class ContinueStmt : Stmt
    {
        public ContinueStmt(SourceSpan span) : base(span) { }
    }

    /// <summary><c>if</c>/<c>elif</c>의 조건과 그 아래 블록.</summary>
    public sealed class IfBranch
    {
        public IfBranch(Expr condition, IReadOnlyList<Stmt> body, SourceSpan span)
        {
            Condition = condition;
            Body = body;
            Span = span;
        }

        public Expr Condition { get; }
        public IReadOnlyList<Stmt> Body { get; }

        /// <summary><c>if</c>/<c>elif</c> 키워드의 위치.</summary>
        public SourceSpan Span { get; }
    }

    /// <summary><c>if</c> / <c>elif</c>... / <c>else</c> (spec-gaps G46).</summary>
    public sealed class IfStmt : Stmt
    {
        public IfStmt(IReadOnlyList<IfBranch> branches, IReadOnlyList<Stmt>? orElse, SourceSpan span) : base(span)
        {
            Branches = branches;
            OrElse = orElse;
        }

        /// <summary>첫 가지가 <c>if</c>, 나머지가 <c>elif</c>.</summary>
        public IReadOnlyList<IfBranch> Branches { get; }

        /// <summary><c>else</c> 블록. 없으면 null.</summary>
        public IReadOnlyList<Stmt>? OrElse { get; }
    }

    /// <summary>이 단계에서 지원하지 않는 문장 (진단은 이미 보고됨).</summary>
    public sealed class UnsupportedStmt : Stmt
    {
        public UnsupportedStmt(string keyword, SourceSpan span) : base(span) => Keyword = keyword;
        public string Keyword { get; }
    }

    public sealed class Module : Node
    {
        public Module(string file, IReadOnlyList<Stmt> body) : base(new SourceSpan(file, 1, 1)) => Body = body;
        public IReadOnlyList<Stmt> Body { get; }
    }
}
