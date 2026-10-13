using System;
using System.Collections.Generic;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Numerics;

namespace Kesa.Engine.Script
{
    /// <summary>진단을 이미 보고하고 현재 문장을 중단할 때 던진다.</summary>
    public sealed class ScriptError : Exception
    {
    }

    /// <summary>내장 함수가 진단을 내는 통로. 진단에는 항상 실행 중인 모드가 붙는다.</summary>
    public sealed class CallContext
    {
        public CallContext(string mod, DiagnosticBag diagnostics)
        {
            Mod = mod;
            Diagnostics = diagnostics;
        }

        public string Mod { get; }
        public DiagnosticBag Diagnostics { get; }

        public void Error(string code, string message, SourceSpan span, string? hint = null,
            IReadOnlyList<SourceSpan>? related = null) =>
            Diagnostics.Error(code, message, span, Mod, hint, related);

        /// <summary>오류를 보고하고 현재 문장을 중단한다.</summary>
        public ScriptError Fail(string code, string message, SourceSpan span, string? hint = null,
            IReadOnlyList<SourceSpan>? related = null)
        {
            Error(code, message, span, hint, related);
            return new ScriptError();
        }
    }

    /// <summary>내장 이름의 출처. 단계(스키마 파일 / Def 파일)마다 다르다.</summary>
    public interface IBuiltins
    {
        Value? Lookup(string name);

        /// <summary>"혹시 …?" 후보.</summary>
        IEnumerable<string> Names { get; }

        /// <summary>
        /// 이 단계에서 쓸 수 없지만 다른 단계에서는 뜻이 있는 이름이면 그 이유 (예: 스키마 파일에서 Def 생성자).
        /// 진단 코드와 메시지를 돌려준다.
        /// </summary>
        (string Code, string Message)? Explain(string name);
    }

    /// <summary>
    /// 모드 하나의 최상위 이름들. 한 모드의 파일들은 실행 순서대로 같은 이름 공간을 쓰고,
    /// 이름은 한 번만 묶을 수 있다 (spec-gaps G20).
    /// </summary>
    public sealed class ModScope
    {
        private readonly Dictionary<string, (Value Value, SourceSpan Span)> _bindings =
            new Dictionary<string, (Value, SourceSpan)>(StringComparer.Ordinal);
        private readonly List<string> _order = new List<string>();

        public bool TryGet(string name, out Value value, out SourceSpan span)
        {
            if (_bindings.TryGetValue(name, out var b))
            {
                value = b.Value;
                span = b.Span;
                return true;
            }
            value = NoneValue.Instance;
            span = SourceSpan.None;
            return false;
        }

        public void Bind(string name, Value value, SourceSpan span)
        {
            _bindings.Add(name, (value, span));
            _order.Add(name);
        }

        /// <summary>묶인 순서대로의 이름.</summary>
        public IReadOnlyList<string> Names => _order;
    }

    /// <summary>
    /// 로드 시점 평가기 (spec S2의 부분집합 중 선언에 필요한 것만).
    /// 최상위 문장: 호출, <c>name = 식</c>, <c>def</c>. 식: 리터럴, 리스트, 맵, 이름, 호출, 타입 표기의 <c>[...]</c>, 수 앞의 부호,
    /// 람다(값으로만 묶고 본문은 평가하지 않음).
    /// 그 밖의 식은 로직 단계에서 지원하며 지금은 <see cref="Codes.EvlUnsupported"/>.
    /// 오류가 난 문장은 건너뛰고 다음 문장을 계속 실행해 오류를 모은다 (spec S1 오류 정책).
    /// </summary>
    public sealed class Interpreter
    {
        public const string InheritName = "inherit";

        private readonly ModScope _scope;
        private readonly IBuiltins _builtins;
        private readonly CallContext _context;

        public Interpreter(ModScope scope, IBuiltins builtins, CallContext context)
        {
            _scope = scope;
            _builtins = builtins;
            _context = context;
        }

        public void Run(Module module)
        {
            foreach (var stmt in module.Body)
            {
                try
                {
                    Execute(stmt);
                }
                catch (ScriptError)
                {
                    // 진단은 이미 보고됨. 다음 문장으로.
                }
            }
        }

        private void Execute(Stmt stmt)
        {
            switch (stmt)
            {
                case ExprStmt es:
                    Evaluate(es.Expr);
                    return;

                case AssignStmt assign:
                    if (assign.Op != "=" || !(assign.Target is NameExpr target))
                    {
                        throw _context.Fail(Codes.EvlUnsupported,
                            "only 'name = value' assignments are allowed at the top level of a content file", assign.Span);
                    }
                    var value = Evaluate(assign.Value);
                    Bind(target.Name, value, target.Span);
                    return;

                case FunctionDef def:
                    Bind(def.Name, new FunctionValue(def, _context.Mod), def.Span);
                    return;

                case UnsupportedStmt _:
                    return; // 파서가 이미 보고함
            }
            throw _context.Fail(Codes.EvlUnsupported, "statement not supported at load time", stmt.Span);
        }

        private void Bind(string name, Value value, SourceSpan span)
        {
            if (name == InheritName || _builtins.Lookup(name) != null || _builtins.Explain(name) != null)
            {
                throw _context.Fail(Codes.EvlNameTaken, "'" + name + "' is a built-in name and cannot be redefined", span);
            }
            if (_scope.TryGet(name, out _, out var first))
            {
                throw _context.Fail(Codes.EvlNameTaken, "'" + name + "' is already defined in this mod", span,
                    related: new[] { first });
            }
            _scope.Bind(name, value, span);
        }

        public Value Evaluate(Expr expr)
        {
            switch (expr)
            {
                case IntLiteral i: return new IntValue(i.Value);
                case FixedLiteral f: return new FixedValue(f.Value);
                case StringLiteral s: return new StrValue(s.Value, s.Span);
                case BoolLiteral b: return b.Value ? BoolValue.True : BoolValue.False;
                case NoneLiteral _: return NoneValue.Instance;
                case ListExpr list: return EvaluateList(list);
                case DictExpr dict: return EvaluateDict(dict);
                case NameExpr name: return Lookup(name);
                case CallExpr call: return EvaluateCall(call);
                case IndexExpr index: return EvaluateIndex(index);
                case UnaryExpr unary: return EvaluateUnary(unary);
                case LambdaExpr lambda: return new FunctionValue(lambda, _context.Mod);
                case UnsupportedExpr _: throw new ScriptError(); // 파서가 이미 보고함
                case AttributeExpr attr:
                    if (ContainsInherit(attr))
                        throw _context.Fail(Codes.EvlUnsupported, "'inherit' can only be used in a Def field value", attr.Span);
                    throw _context.Fail(Codes.EvlUnsupported, "attribute access is not supported at load time yet", attr.Span);
                case BinaryExpr bin:
                    throw _context.Fail(Codes.EvlUnsupported, "operator '" + bin.Op + "' is not supported at load time yet",
                        bin.Span);
            }
            throw _context.Fail(Codes.EvlUnsupported, "expression not supported at load time", expr.Span);
        }

        private Value EvaluateList(ListExpr list)
        {
            var items = new List<Value>(list.Items.Count);
            foreach (var item in list.Items) items.Add(Evaluate(item));
            return new ListValue(items);
        }

        private Value EvaluateDict(DictExpr dict)
        {
            var items = new List<DictItem>(dict.Entries.Count);
            var seenStr = new Dictionary<string, SourceSpan>(StringComparer.Ordinal);
            var seenInt = new Dictionary<long, SourceSpan>();
            foreach (var entry in dict.Entries)
            {
                var key = Evaluate(entry.Key);
                SourceSpan first;
                bool duplicate;
                switch (key)
                {
                    case StrValue s:
                        duplicate = seenStr.TryGetValue(s.Value, out first);
                        if (!duplicate) seenStr.Add(s.Value, entry.Key.Span);
                        break;
                    case IntValue n:
                        duplicate = seenInt.TryGetValue(n.Value, out first);
                        if (!duplicate) seenInt.Add(n.Value, entry.Key.Span);
                        break;
                    default:
                        throw _context.Fail(Codes.EvlType, "dict keys must be str or int, not " + key.KindName,
                            entry.Key.Span);
                }
                if (duplicate)
                {
                    throw _context.Fail(Codes.EvlType, "duplicate dict key", entry.Key.Span, related: new[] { first });
                }
                items.Add(new DictItem(key, Evaluate(entry.Value), entry.Key.Span));
            }
            return new DictValue(items);
        }

        private Value Lookup(NameExpr name)
        {
            if (_scope.TryGet(name.Name, out var value, out _)) return value;
            var builtin = _builtins.Lookup(name.Name);
            if (builtin != null) return builtin;

            if (name.Name == InheritName)
                throw _context.Fail(Codes.EvlUnsupported, "'inherit' can only be used in a Def field value", name.Span);

            var explained = _builtins.Explain(name.Name);
            if (explained != null) throw _context.Fail(explained.Value.Code, explained.Value.Message, name.Span);

            var candidates = new List<string>(_scope.Names);
            candidates.AddRange(_builtins.Names);
            throw _context.Fail(Codes.EvlUnknownName, "unknown name '" + name.Name + "'", name.Span,
                hint: Suggest.Hint(name.Name, candidates));
        }

        private Value EvaluateCall(CallExpr call)
        {
            var callee = Evaluate(call.Callee);
            if (!(callee is CallableValue callable))
            {
                throw _context.Fail(Codes.EvlType,
                    callee is FunctionValue
                        ? "functions cannot be called at load time"
                        : "a " + callee.KindName + " value is not callable", call.Span);
            }

            var args = new List<ArgValue>(call.Arguments.Count);
            foreach (var arg in call.Arguments)
            {
                Value value = callable.AcceptsDeferred && ContainsInherit(arg.Value)
                    ? new DeferredValue(arg.Value)
                    : Evaluate(arg.Value);
                args.Add(new ArgValue(arg.Name, value, arg.Name == null ? arg.Value.Span : arg.Span));
            }
            return callable.Call(_context, call.Span, args);
        }

        private Value EvaluateIndex(IndexExpr index)
        {
            var target = Evaluate(index.Target);
            if (target is ISubscriptable subscriptable)
            {
                var indices = new List<Value>(index.Indices.Count);
                foreach (var i in index.Indices) indices.Add(Evaluate(i));
                return subscriptable.Subscript(_context, index.Span, indices);
            }
            throw _context.Fail(Codes.EvlUnsupported, "indexing a " + target.KindName + " is not supported at load time yet",
                index.Span);
        }

        private Value EvaluateUnary(UnaryExpr unary)
        {
            var operand = Evaluate(unary.Operand);
            if (unary.Op == "-" || unary.Op == "+")
            {
                bool negate = unary.Op == "-";
                switch (operand)
                {
                    case IntValue i:
                        if (!negate) return i;
                        if (i.Value == long.MinValue)
                            throw _context.Fail(Codes.EvlType, "integer out of range", unary.Span);
                        return new IntValue(-i.Value);
                    case FixedValue f:
                        if (!negate) return f;
                        if (f.Value.Raw == long.MinValue)
                            throw _context.Fail(Codes.EvlType, "fixed value out of range", unary.Span);
                        return new FixedValue(Fixed.FromRaw(-f.Value.Raw));
                }
                throw _context.Fail(Codes.EvlType, "unary '" + unary.Op + "' needs a number, not " + operand.KindName,
                    unary.Span);
            }
            throw _context.Fail(Codes.EvlUnsupported, "operator '" + unary.Op + "' is not supported at load time yet",
                unary.Span);
        }

        /// <summary>식 안에 <c>inherit</c> 이름이 쓰였는가.</summary>
        public static bool ContainsInherit(Expr expr)
        {
            switch (expr)
            {
                case NameExpr n: return n.Name == InheritName;
                case AttributeExpr a: return ContainsInherit(a.Target);
                case IndexExpr i:
                    if (ContainsInherit(i.Target)) return true;
                    foreach (var x in i.Indices)
                        if (ContainsInherit(x)) return true;
                    return false;
                case CallExpr c:
                    if (ContainsInherit(c.Callee)) return true;
                    foreach (var a in c.Arguments)
                        if (ContainsInherit(a.Value)) return true;
                    return false;
                case UnaryExpr u: return ContainsInherit(u.Operand);
                case BinaryExpr b: return ContainsInherit(b.Left) || ContainsInherit(b.Right);
                case ListExpr l:
                    foreach (var x in l.Items)
                        if (ContainsInherit(x)) return true;
                    return false;
                case DictExpr d:
                    foreach (var e in d.Entries)
                        if (ContainsInherit(e.Key) || ContainsInherit(e.Value)) return true;
                    return false;
            }
            return false;
        }
    }
}
