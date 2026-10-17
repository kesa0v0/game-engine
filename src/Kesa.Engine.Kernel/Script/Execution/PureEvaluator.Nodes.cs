using System.Collections.Generic;
using Kesa.Engine.Diagnostics;

namespace Kesa.Engine.Script
{
    public sealed partial class PureEvaluator
    {
        /// <summary>
        /// 식 하나를 칸 배치 하나에서 풀어 만든 실행 노드 (G226 ⑦). 노드는 실행기·월드를 모른다: 이름은 지역 칸 번호이거나 자리 캐시를 든 전역·내장이고,
        /// 연산자는 번호다. 그래서 같은 AST를 쓰는 여러 월드가 같은 노드를 나눠 쓴다(속성 자리 캐시와 같다).
        /// <para>
        /// 평가 규칙은 노드로 바꾸기 전의 재귀 실행기와 같다. 노드 하나를 평가할 때 1단계를 쓰고(G101), 오류에는 그 오류가 났을 때 평가 중이던 가장 안쪽
        /// 노드의 위치를 붙인다. 자기 연산이 실패할 수 없는 노드(상수, 지역 변수, 리스트, 람다)는 감싸지 않고, 단계 초과와 "대입 전" 오류는 그 자리에서
        /// 위치를 붙인다. 나머지는 자기 몸체를 감싸 위치를 붙인다: 안쪽 노드가 이미 붙인 오류는 <see cref="Session.Attach"/>가 건너뛴다.
        /// </para>
        /// </summary>
        internal abstract class ExprNode
        {
            protected ExprNode(SourceSpan span) => Span = span;

            public SourceSpan Span { get; }

            public abstract Value Eval(Session s, Frame f);
        }

        /// <summary>AST 식의 실행 노드를 칸 배치마다 하나 기억한다 (<see cref="Expr.Compiled"/>).</summary>
        private sealed class CompiledSite
        {
            public CompiledSite(FrameLayout layout, ExprNode node)
            {
                Layout = layout;
                Node = node;
            }

            public FrameLayout Layout { get; }
            public ExprNode Node { get; }
        }

        /// <summary>식을 칸 배치에서 실행 노드로 만든다 (풀기 결과 → 실행 노드).</summary>
        private static class NodeBuilder
        {
            /// <summary>식의 실행 노드. 같은 배치면 처음 만든 것을 다시 쓴다.</summary>
            public static ExprNode For(Expr expr, FrameLayout layout)
            {
                if (expr.Compiled is CompiledSite site && ReferenceEquals(site.Layout, layout)) return site.Node;
                var node = Build(expr, layout);
                expr.Compiled = new CompiledSite(layout, node);
                return node;
            }

            public static ExprNode Build(Expr expr, FrameLayout layout)
            {
                switch (expr)
                {
                    case IntLiteral i: return new ConstNode(i.Constant, i.Span);
                    case FixedLiteral x: return new ConstNode(x.Constant, x.Span);
                    case StringLiteral s: return new ConstNode(s.Constant, s.Span);
                    case BoolLiteral b: return new ConstNode(b.Value ? BoolValue.True : BoolValue.False, b.Span);
                    case NoneLiteral n: return new ConstNode(NoneValue.Instance, n.Span);
                    case NameExpr name:
                        int slot = layout.SlotOf(name.Name);
                        return slot >= 0 ? new LocalNode(slot, name.Name, name.Span) : (ExprNode)new FreeNode(name.Name, name.Span);
                    case ListExpr list: return new ListNode(All(list.Items, layout), list.Span);
                    case DictExpr dict:
                        var keys = new ExprNode[dict.Entries.Count];
                        var values = new ExprNode[dict.Entries.Count];
                        for (int i = 0; i < keys.Length; i++)
                        {
                            keys[i] = Build(dict.Entries[i].Key, layout);
                            values[i] = Build(dict.Entries[i].Value, layout);
                        }
                        return new DictNode(dict, keys, values);
                    case AttributeExpr attr: return new AttributeNode(attr, Build(attr.Target, layout));
                    case IndexExpr index:
                        return new IndexNode(index, Build(index.Target, layout), index.Indices.Count == 1 ? Build(index.Index, layout) : null);
                    case CallExpr call: return BuildCall(call, layout);
                    case UnaryExpr unary: return new UnaryNode(unary, Build(unary.Operand, layout));
                    case BinaryExpr bin:
                        var left = Build(bin.Left, layout);
                        var right = Build(bin.Right, layout);
                        switch (bin.Operator)
                        {
                            case BinaryOp.And: return new AndNode(left, right, bin.Span);
                            case BinaryOp.Or: return new OrNode(left, right, bin.Span);
                        }
                        return new BinaryNode(bin, left, right);
                    case ConditionalExpr cond:
                        return new ConditionalNode(Build(cond.Condition, layout), Build(cond.WhenTrue, layout), Build(cond.WhenFalse, layout),
                            cond.Span);
                    case LambdaExpr lambda: return new LambdaNode(lambda);
                    case ComprehensionExpr comp: return BuildComprehension(comp, layout);
                }
                return new UnsupportedNode(expr.Span);
            }

            private static ExprNode[] All(IReadOnlyList<Expr> items, FrameLayout layout)
            {
                var nodes = new ExprNode[items.Count];
                for (int i = 0; i < nodes.Length; i++) nodes[i] = Build(items[i], layout);
                return nodes;
            }

            public static CallNode BuildCall(CallExpr call, FrameLayout layout)
            {
                var args = new ExprNode[call.Arguments.Count];
                var names = new string?[call.Arguments.Count];
                bool keywords = false;
                for (int i = 0; i < args.Length; i++)
                {
                    names[i] = call.Arguments[i].Name;
                    keywords |= names[i] != null;
                    args[i] = Build(call.Arguments[i].Value, layout);
                }
                // any(x for ...)·all(x for ...)이 될 수 있는 모양: 부를 때 정말 내장 any·all이면 답이 정해지는 대로 멈춘다 (G189)
                var generator = args.Length == 1 && names[0] == null && args[0] is ComprehensionNode comp &&
                                comp.Kind == ComprehensionKind.Generator
                    ? comp
                    : null;
                return new CallNode(call, Build(call.Callee, layout), args, keywords ? names : null, generator);
            }

            private static ComprehensionNode BuildComprehension(ComprehensionExpr comp, FrameLayout layout)
            {
                // 반복 변수는 이 식만의 이름 공간에 있다: 바깥 이름에 반복 변수를 더한 배치 (파이썬 3)
                if (!layout.TryChild(comp, out var scope))
                {
                    var variables = new List<string>(comp.Clauses.Count);
                    foreach (var clause in comp.Clauses) variables.Add(clause.Variable);
                    scope = layout.Child(comp, variables);
                }
                var clauses = new ClauseNode[comp.Clauses.Count];
                for (int i = 0; i < clauses.Length; i++)
                {
                    var clause = comp.Clauses[i];
                    // 첫 절의 반복할 것은 바깥에서 계산하고, 그 뒤는 모두 이 식의 이름 공간에서 계산한다
                    clauses[i] = new ClauseNode(
                        Build(clause.Iterable, i == 0 ? layout : scope),
                        scope.SlotOf(clause.Variable),
                        All(clause.Conditions, scope));
                }
                return new ComprehensionNode(comp, scope, clauses, Build(comp.Element, scope),
                    comp.Value == null ? null : Build(comp.Value, scope));
            }
        }

        // ---- 이름과 상수 ----

        private sealed class ConstNode : ExprNode
        {
            private readonly Value _value;

            public ConstNode(Value value, SourceSpan span) : base(span) => _value = value;

            public override Value Eval(Session s, Frame f)
            {
                s.Tick(this);
                return _value;
            }
        }

        private sealed class LocalNode : ExprNode
        {
            private readonly int _slot;
            private readonly string _name;

            public LocalNode(int slot, string name, SourceSpan span) : base(span)
            {
                _slot = slot;
                _name = name;
            }

            public override Value Eval(Session s, Frame f)
            {
                s.Tick(this);
                return f.Locals.At(_slot) ?? throw s.Placed(s.Unassigned(_name), this);
            }
        }

        /// <summary>
        /// 지역 변수가 아닌 이름: 모드의 전역, 그다음 내장. 실행 중에 바뀌지 않으므로 같은 실행기(내장 표)·같은 모드(전역)면 처음 찾은 값 그대로다.
        /// </summary>
        private sealed class FreeNode : ExprNode
        {
            private readonly string _name;
            private PureEvaluator? _owner;
            private string? _mod;
            private Value? _value;

            public FreeNode(string name, SourceSpan span) : base(span) => _name = name;

            public override Value Eval(Session s, Frame f)
            {
                s.Tick(this);
                if (ReferenceEquals(_owner, s.Owner) && string.Equals(_mod, f.Function.Mod)) return _value!;
                return Find(s, f);
            }

            private Value Find(Session s, Frame f)
            {
                Value value;
                try
                {
                    value = s.LookupFree(_name, f);
                }
                catch (ScriptRuntimeException e)
                {
                    s.Attach(e, Span);
                    throw;
                }
                _owner = s.Owner;
                _mod = f.Function.Mod;
                _value = value;
                return value;
            }
        }

        private sealed class UnsupportedNode : ExprNode
        {
            public UnsupportedNode(SourceSpan span) : base(span)
            {
            }

            public override Value Eval(Session s, Frame f)
            {
                s.Tick(this);
                throw s.Placed(s.Fail(Codes.RunScript, "this expression is not supported in a logic function"), this);
            }
        }

        // ---- 묶음 ----

        private sealed class ListNode : ExprNode
        {
            private readonly ExprNode[] _items;

            public ListNode(ExprNode[] items, SourceSpan span) : base(span) => _items = items;

            public override Value Eval(Session s, Frame f)
            {
                s.Tick(this);
                var items = new List<Value>(_items.Length);
                foreach (var item in _items) items.Add(item.Eval(s, f));
                return new ListValue(items);
            }
        }

        private sealed class DictNode : ExprNode
        {
            private readonly DictExpr _site;
            private readonly ExprNode[] _keys;
            private readonly ExprNode[] _values;

            public DictNode(DictExpr site, ExprNode[] keys, ExprNode[] values) : base(site.Span)
            {
                _site = site;
                _keys = keys;
                _values = values;
            }

            public override Value Eval(Session s, Frame f)
            {
                try
                {
                    s.Tick();
                    var items = new List<DictItem>(_keys.Length);
                    var result = new DictValue(items);
                    for (int i = 0; i < _keys.Length; i++)
                    {
                        var key = _keys[i].Eval(s, f);
                        if (!(key is StrValue) && !(key is IntValue))
                            throw s.Fail(Codes.RunScript, "dict keys must be str or int, not " + key.KindName);
                        if (PureOps.FindKey(result, key) != null) throw s.Fail(Codes.RunScript, "duplicate dict key");
                        items.Add(new DictItem(key, _values[i].Eval(s, f), _site.Entries[i].Key.Span));
                    }
                    return result;
                }
                catch (ScriptRuntimeException e)
                {
                    s.Attach(e, Span);
                    throw;
                }
            }
        }

        // ---- 속성과 인덱스 ----

        private sealed class AttributeNode : ExprNode
        {
            private readonly AttributeExpr _site;
            private readonly ExprNode _target;

            public AttributeNode(AttributeExpr site, ExprNode target) : base(site.Span)
            {
                _site = site;
                _target = target;
            }

            public override Value Eval(Session s, Frame f)
            {
                try
                {
                    s.Tick();
                    var owner = _target.Eval(s, f);
                    if (owner is ISiteAttributes site) return site.GetAttribute(_site);
                    if (owner is IRuntimeAttributes source) return source.GetAttribute(_site.Name);
                    throw s.Fail(Codes.RunScript, "'" + owner.KindName + "' has no attribute '" + _site.Name + "'");
                }
                catch (ScriptRuntimeException e)
                {
                    s.Attach(e, Span);
                    throw;
                }
            }
        }

        private sealed class IndexNode : ExprNode
        {
            private readonly ExprNode _target;
            private readonly ExprNode? _index;

            public IndexNode(IndexExpr site, ExprNode target, ExprNode? index) : base(site.Span)
            {
                _target = target;
                _index = index;
            }

            public override Value Eval(Session s, Frame f)
            {
                try
                {
                    s.Tick();
                    if (_index == null) throw s.Fail(Codes.RunScript, "an index takes one value");
                    return s.Index(_target.Eval(s, f), _index.Eval(s, f));
                }
                catch (ScriptRuntimeException e)
                {
                    s.Attach(e, Span);
                    throw;
                }
            }
        }

        // ---- 연산 ----

        private sealed class UnaryNode : ExprNode
        {
            private readonly UnaryExpr _site;
            private readonly ExprNode _operand;

            public UnaryNode(UnaryExpr site, ExprNode operand) : base(site.Span)
            {
                _site = site;
                _operand = operand;
            }

            public override Value Eval(Session s, Frame f)
            {
                try
                {
                    s.Tick();
                    var operand = _operand.Eval(s, f);
                    switch (_site.Operator)
                    {
                        case UnaryOp.Negate: return PureOps.Negate(operand);
                        case UnaryOp.Plus: return PureOps.Plus(operand);
                        case UnaryOp.Not: return PureOps.Truthy(operand) ? BoolValue.False : BoolValue.True;
                    }
                    throw s.Fail(Codes.RunScript, "operator '" + _site.Op + "' is not supported in a logic function");
                }
                catch (ScriptRuntimeException e)
                {
                    s.Attach(e, Span);
                    throw;
                }
            }
        }

        private sealed class BinaryNode : ExprNode
        {
            private readonly BinaryExpr _site;
            private readonly BinaryOp _op;
            private readonly ExprNode _left;
            private readonly ExprNode _right;

            public BinaryNode(BinaryExpr site, ExprNode left, ExprNode right) : base(site.Span)
            {
                _site = site;
                _op = site.Operator;
                _left = left;
                _right = right;
            }

            public override Value Eval(Session s, Frame f)
            {
                try
                {
                    s.Tick();
                    var left = _left.Eval(s, f);
                    var right = _right.Eval(s, f);
                    return s.Binary(_op, left, right, _site);
                }
                catch (ScriptRuntimeException e)
                {
                    s.Attach(e, Span);
                    throw;
                }
            }
        }

        /// <summary><c>and</c>: 파이썬처럼 결정한 쪽의 피연산자 값을 그대로 돌려주고, 오른쪽은 필요할 때만 평가한다 (G104).</summary>
        private sealed class AndNode : ExprNode
        {
            private readonly ExprNode _left;
            private readonly ExprNode _right;

            public AndNode(ExprNode left, ExprNode right, SourceSpan span) : base(span)
            {
                _left = left;
                _right = right;
            }

            public override Value Eval(Session s, Frame f)
            {
                try
                {
                    s.Tick();
                    var left = _left.Eval(s, f);
                    return PureOps.Truthy(left) ? _right.Eval(s, f) : left;
                }
                catch (ScriptRuntimeException e)
                {
                    s.Attach(e, Span);
                    throw;
                }
            }
        }

        private sealed class OrNode : ExprNode
        {
            private readonly ExprNode _left;
            private readonly ExprNode _right;

            public OrNode(ExprNode left, ExprNode right, SourceSpan span) : base(span)
            {
                _left = left;
                _right = right;
            }

            public override Value Eval(Session s, Frame f)
            {
                try
                {
                    s.Tick();
                    var left = _left.Eval(s, f);
                    return PureOps.Truthy(left) ? left : _right.Eval(s, f);
                }
                catch (ScriptRuntimeException e)
                {
                    s.Attach(e, Span);
                    throw;
                }
            }
        }

        private sealed class ConditionalNode : ExprNode
        {
            private readonly ExprNode _condition;
            private readonly ExprNode _whenTrue;
            private readonly ExprNode _whenFalse;

            public ConditionalNode(ExprNode condition, ExprNode whenTrue, ExprNode whenFalse, SourceSpan span) : base(span)
            {
                _condition = condition;
                _whenTrue = whenTrue;
                _whenFalse = whenFalse;
            }

            public override Value Eval(Session s, Frame f)
            {
                try
                {
                    s.Tick();
                    return PureOps.Truthy(_condition.Eval(s, f)) ? _whenTrue.Eval(s, f) : _whenFalse.Eval(s, f);
                }
                catch (ScriptRuntimeException e)
                {
                    s.Attach(e, Span);
                    throw;
                }
            }
        }

        // ---- 함수 ----

        private sealed class LambdaNode : ExprNode
        {
            private readonly LambdaExpr _lambda;

            public LambdaNode(LambdaExpr lambda) : base(lambda.Span) => _lambda = lambda;

            public override Value Eval(Session s, Frame f)
            {
                s.Tick(this);
                // 만든 함수의 지역 변수 자체를 들고 있다가 부를 때 읽는다: 파이썬처럼 늦게 묶인다
                return new ClosureValue(new FunctionValue(_lambda, f.Function.Mod), f.Locals);
            }
        }

        /// <summary>식 안의 호출: 끝까지 실행해 값을 낸다. 쓰기 함수는 여기서 쓰기 모드가 되지 않는다 (G117·G135).</summary>
        private sealed class CallNode : ExprNode
        {
            private readonly CallExpr _site;
            private readonly ExprNode _callee;
            private readonly ExprNode[] _args;

            /// <summary>인자마다 키워드 이름. 키워드 인자가 없으면 null.</summary>
            private readonly string?[]? _names;

            private readonly ComprehensionNode? _generator;
            private readonly int _positionalCount;

            public CallNode(CallExpr site, ExprNode callee, ExprNode[] args, string?[]? names, ComprehensionNode? generator)
                : base(site.Span)
            {
                _site = site;
                _callee = callee;
                _args = args;
                _names = names;
                _generator = generator;
                _positionalCount = args.Length;
                if (names != null)
                    foreach (var name in names)
                        if (name != null) _positionalCount--;
            }

            public override Value Eval(Session s, Frame f)
            {
                try
                {
                    s.Tick();
                    var callee = Parts(s, f, out var positional, out var keywords);
                    return s.Invoke(callee, positional, keywords, _site.Span);
                }
                catch (ScriptRuntimeException e)
                {
                    s.Attach(e, Span);
                    throw;
                }
            }

            /// <summary>부를 것과 인자를 계산한다 (이 노드의 단계는 세지 않는다: 문장 수준 호출은 문장이 센다).</summary>
            public Value Parts(Session s, Frame f, out Value[] positional, out IReadOnlyDictionary<string, Value> keywords)
            {
                var callee = _callee.Eval(s, f);
                // any(x for ...)·all(x for ...): 답이 정해지면 멈춘다 (파이썬과 같다, G189). 내장에는 정해진 답을 낼 원소 하나만 넘긴다
                if (_generator != null && callee is PureBuiltin truth &&
                    (truth.Name == PureBuiltins.AnyName || truth.Name == PureBuiltins.AllName))
                {
                    bool stopOn = truth.Name == PureBuiltins.AnyName;
                    bool stopped = _generator.Decides(s, f, stopOn);
                    var decided = stopped ? new List<Value> { stopOn ? BoolValue.True : BoolValue.False } : new List<Value>();
                    positional = new Value[] { new ListValue(decided) };
                    keywords = Session.NoKeywords;
                    return callee;
                }
                // 인자 목록은 처음부터 맞는 크기의 배열이다 (위치 인자 수는 노드를 만들 때 안다)
                positional = _positionalCount == 0 ? System.Array.Empty<Value>() : new Value[_positionalCount];
                SortedDictionary<string, Value>? named = null; // 키워드 인자가 있을 때만 만든다
                int p = 0;
                for (int i = 0; i < _args.Length; i++)
                {
                    var value = _args[i].Eval(s, f);
                    string? name = _names?[i];
                    if (name == null) positional[p++] = value;
                    else (named ??= new SortedDictionary<string, Value>(System.StringComparer.Ordinal))[name] = value;
                }
                keywords = named ?? Session.NoKeywords;
                return callee;
            }
            }

        // ---- 컴프리헨션 ----

        private sealed class ClauseNode
        {
            public ClauseNode(ExprNode iterable, int slot, ExprNode[] conditions)
            {
                Iterable = iterable;
                Slot = slot;
                Conditions = conditions;
            }

            public ExprNode Iterable { get; }

            /// <summary>반복 변수의 칸 (이 식의 이름 공간 배치에서).</summary>
            public int Slot { get; }

            public ExprNode[] Conditions { get; }
        }

        /// <summary>
        /// 컴프리헨션 (G189). 리스트·제너레이터 식은 리스트를 만들고(제너레이터 식을 받는 사용자 함수는 리스트로 받는다), 딕트는 같은 키면
        /// 파이썬처럼 처음 자리에 나중 값을 둔다. 반복 변수는 이 식만의 이름 공간에 있고, 안에서 만든 람다는 그 이름 공간을 들고 간다(파이썬처럼
        /// 마지막 값으로 늦게 묶인다). 한 바퀴마다 단계를 센다 (G101).
        /// </summary>
        private sealed class ComprehensionNode : ExprNode
        {
            private readonly ComprehensionExpr _site;
            private readonly FrameLayout _scope;
            private readonly ClauseNode[] _clauses;
            private readonly ExprNode _element;
            private readonly ExprNode? _value;

            public ComprehensionNode(ComprehensionExpr site, FrameLayout scope, ClauseNode[] clauses, ExprNode element, ExprNode? value)
                : base(site.Span)
            {
                _site = site;
                _scope = scope;
                _clauses = clauses;
                _element = element;
                _value = value;
            }

            public ComprehensionKind Kind => _site.Kind;

            /// <summary>조건을 통과한 조합마다 무엇을 하는가.</summary>
            private enum Mode
            {
                List,
                Dict,
                Decide,
            }

            /// <summary>한 번의 평가가 모으는 것.</summary>
            private sealed class Sink
            {
                public Mode Mode;
                public List<Value>? Items;
                public List<DictItem>? Entries;
                public bool StopOn;
                public bool Stopped;
            }

            public override Value Eval(Session s, Frame f)
            {
                try
                {
                    s.Tick();
                    if (_site.Kind == ComprehensionKind.Dict)
                    {
                        var entries = new List<DictItem>();
                        Walk(s, f, new Sink { Mode = Mode.Dict, Entries = entries });
                        return new DictValue(entries);
                    }
                    var items = new List<Value>();
                    Walk(s, f, new Sink { Mode = Mode.List, Items = items });
                    return new ListValue(items);
                }
                catch (ScriptRuntimeException e)
                {
                    s.Attach(e, Span);
                    throw;
                }
            }

            /// <summary>
            /// <c>any</c>(<paramref name="stopOn"/> 참)·<c>all</c>(거짓)의 제너레이터: 원소의 참/거짓이 <paramref name="stopOn"/>인 것이 나오면 멈추고 true.
            /// 이 식 자체의 단계는 세지 않는다(부르는 쪽이 센다).
            /// </summary>
            public bool Decides(Session s, Frame f, bool stopOn)
            {
                var sink = new Sink { Mode = Mode.Decide, StopOn = stopOn };
                Walk(s, f, sink);
                return sink.Stopped;
            }

            private void Walk(Session s, Frame outer, Sink sink)
            {
                // 아직 값이 없는 바깥 지역 변수가 반복 변수와 이름이 같으면 사본에서도 비어 있다: 반복이 시작되기 전 읽으면 "대입 전" 오류가 맞다
                var scope = new Frame(outer.Function, outer.Locals.ChildTable(_scope))
                {
                    CallSite = outer.CallSite,
                };
                Clause(s, 0, outer, scope, sink);
            }

            private bool Clause(Session s, int index, Frame outer, Frame scope, Sink sink)
            {
                if (index == _clauses.Length) return Each(s, scope, sink);
                var clause = _clauses[index];
                var iterable = clause.Iterable.Eval(s, index == 0 ? outer : scope);
                IReadOnlyList<Value>? items = null;
                RangeValue? range = null;
                switch (iterable)
                {
                    case ListValue list: items = list.Items; break;
                    case DictValue dict:
                        var keys = new List<Value>(dict.Items.Count);
                        foreach (var item in dict.Items) keys.Add(item.Key);
                        items = keys;
                        break;
                    case RangeValue r: range = r; break;
                    case IRuntimeIterable runtime: items = runtime.Iterate(); break;
                    default:
                        throw s.Fail(Codes.RunScript, "'" + iterable.KindName + "' is not iterable",
                            "loop over a list, a dict or range(...)");
                }
                long count = range?.Count ?? items!.Count;
                for (long i = 0; i < count; i++)
                {
                    s.Tick();
                    scope.Locals.Set(clause.Slot, range != null ? new IntValue(range.At(i)) : items![(int)i]);
                    bool pass = true;
                    foreach (var condition in clause.Conditions)
                    {
                        if (PureOps.Truthy(condition.Eval(s, scope))) continue;
                        pass = false;
                        break;
                    }
                    if (pass && !Clause(s, index + 1, outer, scope, sink)) return false;
                }
                return true;
            }

            /// <summary>조건을 통과한 조합 하나. 거짓을 돌려주면 멈춘다.</summary>
            private bool Each(Session s, Frame scope, Sink sink)
            {
                switch (sink.Mode)
                {
                    case Mode.List:
                        sink.Items!.Add(_element.Eval(s, scope));
                        return true;
                    case Mode.Dict:
                        var key = _element.Eval(s, scope);
                        if (!(key is StrValue) && !(key is IntValue))
                            throw s.Fail(Codes.RunScript, "dict keys must be str or int, not " + key.KindName);
                        var value = _value!.Eval(s, scope);
                        var entries = sink.Entries!;
                        int at = entries.FindIndex(e => PureOps.ValuesEqual(e.Key, key));
                        if (at >= 0) entries[at] = new DictItem(entries[at].Key, value, entries[at].Span);
                        else entries.Add(new DictItem(key, value, _site.Element.Span));
                        return true;
                    default:
                        if (PureOps.Truthy(_element.Eval(s, scope)) != sink.StopOn) return true;
                        sink.Stopped = true;
                        return false;
                }
            }
        }
    }
}
