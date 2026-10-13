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
    /// <c>def name(params) -> ret:</c>. 지금은 머리부만 읽고 본문은 건너뛴다: 로딩 단계는 함수를
    /// 값으로 묶기만 하고 호출하지 않는다 (spec-gaps G17). 본문은 식 언어 단계에서 파싱한다.
    /// </summary>
    public sealed class FunctionDef : Stmt
    {
        public FunctionDef(string name, IReadOnlyList<Parameter> parameters, Expr? returns, SourceSpan span) : base(span)
        {
            Name = name;
            Parameters = parameters;
            Returns = returns;
        }

        public string Name { get; }
        public IReadOnlyList<Parameter> Parameters { get; }
        public Expr? Returns { get; }
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
