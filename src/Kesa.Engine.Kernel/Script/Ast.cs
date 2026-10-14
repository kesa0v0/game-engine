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
    }

    public sealed class NameExpr : Expr
    {
        public NameExpr(string name, SourceSpan span) : base(span) => Name = name;
        public string Name { get; }
    }

    public sealed class IntLiteral : Expr
    {
        public IntLiteral(long value, SourceSpan span) : base(span) => Value = value;
        public long Value { get; }
    }

    public sealed class FixedLiteral : Expr
    {
        public FixedLiteral(Fixed value, SourceSpan span) : base(span) => Value = value;
        public Fixed Value { get; }
    }

    public sealed class StringLiteral : Expr
    {
        public StringLiteral(string value, SourceSpan span) : base(span) => Value = value;
        public string Value { get; }
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
        }

        /// <summary>"-", "+", "~", "not"</summary>
        public string Op { get; }
        public Expr Operand { get; }
    }

    public sealed class BinaryExpr : Expr
    {
        public BinaryExpr(string op, Expr left, Expr right, SourceSpan span) : base(span)
        {
            Op = op;
            Left = left;
            Right = right;
        }

        /// <summary>산술·비트·비교·논리 연산자. "not in", "is not"은 공백 하나로 잇는다.</summary>
        public string Op { get; }
        public Expr Left { get; }
        public Expr Right { get; }
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
        }

        public Expr Target { get; }

        /// <summary>"=" 또는 복합 대입 연산자 ("+=" 등).</summary>
        public string Op { get; }
        public Expr Value { get; }
    }

    public sealed class Parameter
    {
        public Parameter(string name, Expr? annotation, SourceSpan span)
        {
            Name = name;
            Annotation = annotation;
            Span = span;
        }

        public string Name { get; }
        public Expr? Annotation { get; }
        public SourceSpan Span { get; }
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

        public string Name { get; }
        public IReadOnlyList<Parameter> Parameters { get; }
        public Expr? Returns { get; }
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
