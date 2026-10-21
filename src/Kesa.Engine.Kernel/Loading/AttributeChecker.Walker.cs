using System;
using System.Collections.Generic;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Schema;
using Kesa.Engine.Script;

namespace Kesa.Engine.Loading
{
    internal static partial class AttributeChecker
    {
        /// <summary>
        /// 함수 하나의 본문을 훑는다. 지역 변수의 타입은 대입한 값들의 타입을 합친 것이고(흐름을 따지지 않는다), 대입이 서로의 타입에
        /// 기대므로(<c>c = u.Food</c>, <c>u = top</c>) 바뀌지 않을 때까지 되풀이한 뒤 한 번 더 훑으며 오류를 낸다.
        /// </summary>
        private sealed class Walker
        {
            private const int MaxRounds = 16;

            private readonly SchemaSet _schema;
            private readonly DefRegistry _defs;
            private readonly ModNames _globals;
            private readonly StageDiagnostics? _stage;
            private readonly string _mod;

            /// <summary>순환 검사(G229)면 자기 엔티티의 <c>derived</c> 읽기를 모으는 곳. 이때는 이름 오류를 내지 않는다(본 검사가 낸다).</summary>
            private readonly DerivedReads? _reads;

            /// <summary>이번 바퀴에 지역 변수마다 대입한 값의 타입 (합친 것).</summary>
            private Dictionary<string, StaticType?> _assigned = new Dictionary<string, StaticType?>(StringComparer.Ordinal);

            /// <summary>모드 함수 호출의 반환 타입 (G231). 없으면 호출 결과는 모름이다.</summary>
            private readonly ReturnTypes? _returns;

            /// <summary>마지막 바퀴인가: 이때만 오류를 낸다.</summary>
            private bool _report;

            public Walker(SchemaSet schema, DefRegistry defs, ModNames globals, StageDiagnostics? stage, string mod,
                DerivedReads? reads = null, ReturnTypes? returns = null)
            {
                _schema = schema;
                _defs = defs;
                _globals = globals;
                _stage = stage;
                _mod = mod;
                _reads = reads;
                _returns = returns;
            }

            /// <summary>마지막 바퀴이고 이름 오류를 내는 검사인가 (순환 검사·반환 타입 훑기는 내지 않는다).</summary>
            private bool Reporting => _report && _stage != null && _reads == null;

            /// <summary>마지막 바퀴에서 본 <c>return</c> 값들의 타입을 합친 것 (값 없는 <c>return</c>은 None). 람다는 본문의 타입.</summary>
            public StaticType? Returned { get; private set; }

            public void Function(FunctionValue function, IReadOnlyList<KeyValuePair<string, StaticType?>> parameters)
            {
                var locals = function.Definition != null
                    ? FunctionLocals.Of(function.Definition)
                    : FunctionLocals.Parameters(function.Parameters);
                var given = new Dictionary<string, StaticType?>(StringComparer.Ordinal);
                foreach (var p in parameters) given[p.Key] = p.Value;
                var scope = new SortedDictionary<string, StaticType?>(StringComparer.Ordinal);
                foreach (var name in locals) scope[name] = given.TryGetValue(name, out var t) ? t : null;

                for (int round = 0; ; round++)
                {
                    _assigned = new Dictionary<string, StaticType?>(StringComparer.Ordinal);
                    Body(function, scope);
                    bool changed = false;
                    foreach (var name in locals)
                    {
                        if (!_assigned.TryGetValue(name, out var assigned)) continue;
                        var before = scope[name];
                        // 늘 넓어지기만 하므로(합치기) 끝난다. 끝나지 않을 만큼 길면 모름으로 둔다
                        var after = round < MaxRounds ? StaticType.Join(before, assigned) : StaticType.Unknown;
                        if (before == null ? after == null : after != null && before.Same(after)) continue;
                        scope[name] = after;
                        changed = true;
                    }
                    if (!changed) break;
                }

                _report = true;
                Body(function, scope);
            }

            private void Body(FunctionValue function, SortedDictionary<string, StaticType?> scope)
            {
                if (function.Definition != null)
                {
                    Block(function.Definition.Body, scope);
                    return;
                }
                var type = Type(function.Lambda!.Body, scope);
                if (_report) Returned = type;
            }

            private void Assign(string name, StaticType? type) =>
                _assigned[name] = StaticType.Join(_assigned.TryGetValue(name, out var known) ? known : null, type);

            private void Block(IReadOnlyList<Stmt> body, SortedDictionary<string, StaticType?> scope)
            {
                foreach (var stmt in body)
                {
                    switch (stmt)
                    {
                        case ExprStmt es:
                            Type(es.Expr, scope);
                            break;
                        case AssignStmt assign:
                            var value = Type(assign.Value, scope);
                            if (assign.Target is NameExpr name) Assign(name.Name, assign.Op == "=" ? value : StaticType.Unknown);
                            else Type(assign.Target, scope); // 상태 쓰기(u.Food.amount = ...)의 이름도 읽기와 같다
                            break;
                        case IfStmt ifs:
                            foreach (var branch in ifs.Branches)
                            {
                                Type(branch.Condition, scope);
                                Block(branch.Body, scope);
                            }
                            if (ifs.OrElse != null) Block(ifs.OrElse, scope);
                            break;
                        case ForStmt loop:
                            Assign(loop.Target.Name, Element(Type(loop.Iterable, scope)));
                            Block(loop.Body, scope);
                            break;
                        case WhileStmt loop:
                            Type(loop.Condition, scope);
                            Block(loop.Body, scope);
                            break;
                        case WithStmt with:
                            if (!(with.Context is NameExpr context && context.Name == FunctionChecker.PreconditionsName))
                                Type(with.Context, scope);
                            Block(with.Body, scope);
                            break;
                        case ReturnStmt ret:
                            var returned = ret.Value != null ? Type(ret.Value, scope) : StaticType.None;
                            if (_report) Returned = StaticType.Join(Returned, returned ?? StaticType.Unknown);
                            break;
                    }
                }
            }

            private static StaticType? Element(StaticType? iterable) =>
                iterable == null ? null : iterable is GroupType group ? group.Element :
                iterable is DefIdMapType ? DefIdStatic.Instance : StaticType.Unknown;

            /// <summary>
            /// <c>:</c> 없는 문자열 리터럴을 DefId 값과 비교하거나 그것으로 찾는 곳 (G261): 값은 늘 전체 이름이라 맞지 않는다. 경고다.
            /// </summary>
            private void BareDefId(Expr literal)
            {
                if (!Reporting || !(literal is StringLiteral s) || s.Value.Length == 0 || DefIds.IsFull(s.Value)) return;
                _stage!.Warning(_mod, Codes.LnkBareDefId,
                    "a Def id value is always a full id like '" + DefIds.Resolve(s.Value, _mod) + "', so it never equals \"" + s.Value + "\"",
                    s.Span, "write defid(\"" + s.Value + "\") or \"" + DefIds.Resolve(s.Value, _mod) + "\"");
            }

            /// <summary>비교·<c>in</c>에서 DefId 값과 맨 리터럴이 만나는가 (G261).</summary>
            private void CheckDefIdComparison(BinaryExpr bin, StaticType? left, StaticType? right)
            {
                switch (bin.Op)
                {
                    case "==":
                    case "!=":
                        if (left is DefIdStatic) BareDefId(bin.Right);
                        if (right is DefIdStatic) BareDefId(bin.Left);
                        break;
                    case "in":
                    case "not in":
                        if (right is DefIdMapType || right is GroupType g && g.Element is DefIdStatic) BareDefId(bin.Left);
                        if (left is DefIdStatic && bin.Right is ListExpr list)
                            foreach (var item in list.Items) BareDefId(item);
                        break;
                }
            }

            /// <summary>이름이 지역 변수나 모드의 최상위 이름이라 내장을 가렸는가 (G112).</summary>
            private bool Shadowed(string name, SortedDictionary<string, StaticType?> scope) =>
                scope.ContainsKey(name) || _globals.All.Contains(name);

            /// <summary>식의 타입. null은 아직 정보가 없다는 뜻이다 (대입을 모으는 중).</summary>
            private StaticType? Type(Expr expr, SortedDictionary<string, StaticType?> scope)
            {
                switch (expr)
                {
                    case NameExpr name:
                        if (scope.TryGetValue(name.Name, out var local)) return local;
                        return _globals.Constructors.TryGetValue(name.Name, out var kind)
                            ? new ConstructorType(name.Name, kind)
                            : StaticType.Unknown;
                    case NoneLiteral _:
                        return StaticType.None;
                    case AttributeExpr attr:
                        return Attribute(Type(attr.Target, scope), attr);
                    case CallExpr call:
                        return Call(call, scope);
                    case IndexExpr index:
                        var target = Type(index.Target, scope);
                        foreach (var i in index.Indices) Type(i, scope);
                        switch (target)
                        {
                            case null: return null;
                            case RowsType rows: return new ComponentStatic(rows.Component, rows.Owner, true);
                            case GroupType group: return group.Element;
                            case DefIdMapType _:
                                if (index.Indices.Count == 1) BareDefId(index.Indices[0]);
                                return StaticType.Unknown;
                        }
                        return StaticType.Unknown;
                    case BinaryExpr bin:
                        CheckDefIdComparison(bin, Type(bin.Left, scope), Type(bin.Right, scope));
                        return StaticType.Unknown;
                    case UnaryExpr unary:
                        Type(unary.Operand, scope);
                        return StaticType.Unknown;
                    case ConditionalExpr cond:
                        Type(cond.Condition, scope);
                        return StaticType.Join(Type(cond.WhenTrue, scope), Type(cond.WhenFalse, scope));
                    case ListExpr list:
                        foreach (var item in list.Items) Type(item, scope);
                        return StaticType.Unknown;
                    case DictExpr dict:
                        foreach (var entry in dict.Entries)
                        {
                            Type(entry.Key, scope);
                            Type(entry.Value, scope);
                        }
                        return StaticType.Unknown;
                    case LambdaExpr lambda:
                        Lambda(lambda, scope, null);
                        return StaticType.Unknown;
                    case ComprehensionExpr comp:
                        return Comprehension(comp, scope);
                }
                return StaticType.Unknown;
            }

            /// <summary>함수 안의 람다: 바깥 지역 변수를 보고, 매개변수는 문맥이 주면 그 타입이다.</summary>
            private void Lambda(LambdaExpr lambda, SortedDictionary<string, StaticType?> scope, StaticType? first)
            {
                var inner = new SortedDictionary<string, StaticType?>(scope, StringComparer.Ordinal);
                for (int i = 0; i < lambda.Parameters.Count; i++)
                    inner[lambda.Parameters[i].Name] = i == 0 && first != null ? first : StaticType.Unknown;
                Type(lambda.Body, inner);
            }

            /// <summary>반복 변수는 그 식 안에서만 보인다 (파이썬 3). 리스트 컴프리헨션은 원소의 묶음이다.</summary>
            private StaticType? Comprehension(ComprehensionExpr comp, SortedDictionary<string, StaticType?> scope)
            {
                var inner = new SortedDictionary<string, StaticType?>(scope, StringComparer.Ordinal);
                foreach (var clause in comp.Clauses)
                {
                    inner[clause.Variable] = Element(Type(clause.Iterable, inner));
                    foreach (var condition in clause.Conditions) Type(condition, inner);
                }
                var element = Type(comp.Element, inner);
                if (comp.Value != null) Type(comp.Value, inner);
                if (comp.Kind != ComprehensionKind.List) return StaticType.Unknown;
                return element == null ? null : new GroupType(element);
            }

            private StaticType? Call(CallExpr call, SortedDictionary<string, StaticType?> scope)
            {
                string? builtin = call.Callee is NameExpr callee && !Shadowed(callee.Name, scope) ? callee.Name : null;

                // choose(p, where=lambda x: ...)의 x는 p의 후보다 (G183)
                StaticType? candidate = null;
                if (builtin == FunctionChecker.ChooseName && call.Arguments.Count > 0)
                    candidate = Type(call.Arguments[0].Value, scope) as EntityType;
                else
                    Type(call.Callee, scope);
                var argTypes = new List<StaticType?>();
                for (int i = candidate != null ? 1 : 0; i < call.Arguments.Count; i++)
                {
                    var arg = call.Arguments[i];
                    if (candidate != null && arg.Name == FunctionChecker.WhereName && arg.Value is LambdaExpr where)
                        Lambda(where, scope, candidate);
                    else
                        argTypes.Add(Type(arg.Value, scope));
                }

                // 순환 검사: 자기 엔티티를 넘겨받은 모드 함수도 읽는다 (helper(h))
                if (_reads != null && _report && candidate == null && call.Callee is NameExpr fn && !scope.ContainsKey(fn.Name))
                    _reads.Follow(_mod, fn.Name, call.Arguments, argTypes, call.Span);

                if (builtin == PureBuiltins.QueryName) return new GroupType(new EntityType(QueriedType(call)));
                if (builtin == PureBuiltins.DefDataName) return DefDataStatic.Instance;
                if (builtin == PureBuiltins.SpawnName)
                {
                    DefType? type = null;
                    if (call.Arguments.Count > 0 && call.Arguments[0].Value is StringLiteral id &&
                        _defs.TryGet(DefIds.Resolve(id.Value, _mod), out var record))
                        type = record.Type;
                    return new EntityType(type);
                }
                // 같은 모드의 함수: 적은 반환 타입이나 이 인자들로 본 본문의 return (G231)
                if (_returns != null && call.Callee is NameExpr own && !scope.ContainsKey(own.Name) && _globals.All.Contains(own.Name) &&
                    !_globals.Constructors.ContainsKey(own.Name))
                    return _returns.Of(_mod, own.Name, call.Arguments, argTypes);
                return StaticType.Unknown;
            }

            /// <summary><c>query("Unit", "Wounded")</c>에 리터럴로 적은 Def 타입 (하나까지, G192). 없으면 아무 엔티티.</summary>
            private DefType? QueriedType(CallExpr call)
            {
                foreach (var arg in call.Arguments)
                    if (arg.Value is StringLiteral s && _schema.TryGetType(s.Value, out var type))
                        return type;
                return null;
            }

            // ---- 속성 이름: 실행(World.EntityAttribute, ComponentAttribute, DefConst)과 같은 규칙 ----

            private StaticType? Attribute(StaticType? target, AttributeExpr attr)
            {
                switch (target)
                {
                    case null:
                        return null;
                    case EntityType entity:
                        return EntityAttribute(entity, attr);
                    case ComponentStatic component:
                        return ComponentAttribute(component, attr);
                    case RowsType rows: // 줄 키 (h.Skill.Medicine): 이름을 보지 않는다
                        return new ComponentStatic(rows.Component, rows.Owner, true);
                    case DefConstsType consts:
                        return DefConst(consts, attr);
                    case DefDataStatic _:
                        // defdata(k).Role: Def 타입 이름 (그 타입이 아니면 실행 중 None, G107과 같다)
                        if (_schema.TryGetType(attr.Name, out var dataType)) return new DefConstsType(dataType);
                        if (Reporting)
                        {
                            var types = new List<string>();
                            foreach (var t in _schema.Types) types.Add(t.Name);
                            Error("defdata(...) has no Def type '" + attr.Name + "'", attr, types, schemaNames: false);
                        }
                        return StaticType.Unknown;
                    case ConstructorType constructor:
                        return ConstructorAttribute(constructor, attr);
                }
                return StaticType.Unknown;
            }

            /// <summary>생성자에는 속성이 없다. 같은 이름의 엔진 내장을 쓰려던 것이면 그렇다고 알린다 (G230).</summary>
            private StaticType? ConstructorAttribute(ConstructorType constructor, AttributeExpr attr)
            {
                if (!Reporting) return StaticType.Unknown;
                string hint = PureBuiltins.IsRuntimeName(constructor.Name)
                    ? "this mod sees a " + constructor.Kind + " named '" + constructor.Name + "', so the built-in " + constructor.Name +
                      " is not available here; rename the " + constructor.Kind + " (spec-gaps G230)"
                    : "make a value first, like " + constructor.Name + "(...), and read it from the entity or the signal";
                _stage!.Error(_mod, Codes.EvlUnknownName,
                    "'" + constructor.Name + "' here is the " + constructor.Kind + " constructor, which has no attribute '" + attr.Name + "'",
                    attr.Span, hint);
                return StaticType.Unknown;
            }

            /// <summary>
            /// 스키마의 컴포넌트(붙어 있지 않으면 실행 중 None), Def 타입 이름(그 타입이 아니면 None), 그 타입의 역방향 컬렉션.
            /// Def 타입을 모르면 어느 타입의 역방향 컬렉션이든 받는다.
            /// </summary>
            private StaticType? EntityAttribute(EntityType entity, AttributeExpr attr)
            {
                string name = attr.Name;
                if (_schema.TryGetComponent(name, out var component)) return View(component, entity.Type, entity.Self);
                if (_schema.TryGetType(name, out var defType)) return new DefConstsType(defType);
                if (name == "defid") return DefIdStatic.Instance;
                foreach (var type in Owners(entity.Type))
                foreach (var inverse in type.Inverses)
                    if (inverse.Name == name)
                        return new GroupType(new EntityType(null));

                if (!Reporting) return StaticType.Unknown;
                var candidates = new List<string>();
                foreach (var type in Owners(entity.Type))
                {
                    candidates.Add(type.Name);
                    foreach (var slot in type.Components) candidates.Add(slot.Component.Name);
                    foreach (var inverse in type.Inverses) candidates.Add(inverse.Name);
                }
                if (entity.Type == null)
                    foreach (var c in _schema.Components) candidates.Add(c.Name);
                string what = entity.Type == null ? "an entity" : "a " + entity.Type.Name + " entity";
                Error(what + " has no component or collection '" + name + "'", attr, candidates);
                return StaticType.Unknown;
            }

            /// <summary>
            /// 자기 멤버, <c>entity</c>, 줄이면 <c>key</c>, 형제 컴포넌트, Def 타입 이름 순 (<c>World.ComponentAttribute</c>).
            /// </summary>
            private StaticType? ComponentAttribute(ComponentStatic view, AttributeExpr attr)
            {
                string name = attr.Name;
                var component = view.Component;
                if (component.TryGetMember(name, out var member))
                {
                    if (_reads != null && _report && view.Self && member.Kind == MemberKind.Derived)
                        _reads.Add(component, member, attr.Span);
                    return FromType(member.Spec.Type, _schema);
                }
                if (name == "entity") return new EntityType(view.Owner, view.Self);
                if (name == "key" && view.Row)
                    return component.KeyKind == KeyKind.Entity ? new EntityType(component.Key) : StaticType.Unknown;
                if (_schema.TryGetComponent(name, out var sibling)) return View(sibling, view.Owner, view.Self);
                if (_schema.TryGetType(name, out var defType)) return new DefConstsType(defType);
                if (name == "defid") return DefIdStatic.Instance;

                if (!Reporting) return StaticType.Unknown;
                var candidates = new List<string> { "entity" };
                if (view.Row) candidates.Add("key");
                foreach (var m in component.Members) candidates.Add(m.Name);
                foreach (var type in Owners(view.Owner))
                {
                    candidates.Add(type.Name);
                    foreach (var slot in type.Components) candidates.Add(slot.Component.Name);
                }
                if (view.Owner == null)
                    foreach (var c in _schema.Components) candidates.Add(c.Name);
                Error("'" + component.Name + "' has no member, sibling component or property '" + name + "'", attr, candidates);
                return StaticType.Unknown;
            }

            private StaticType? DefConst(DefConstsType consts, AttributeExpr attr)
            {
                foreach (var decl in consts.Type.Consts)
                    if (decl.Name == attr.Name)
                        return FromType(decl.Spec.Type, _schema);

                if (!Reporting) return StaticType.Unknown;
                var names = new List<string>();
                foreach (var decl in consts.Type.Consts) names.Add(decl.Name);
                Error("Def type '" + consts.Type.Name + "' has no const '" + attr.Name + "'", attr, names, schemaNames: false);
                return StaticType.Unknown;
            }

            /// <summary>
            /// 엔티티의 컴포넌트. 키 컴포넌트는 줄 묶음이고, 그 줄은 계산 중인 줄과 다를 수 있어 자기 것으로 치지 않는다(G229).
            /// </summary>
            private static StaticType View(ComponentType component, DefType? owner, bool self) =>
                component.IsKeyed ? new RowsType(component, owner) : (StaticType)new ComponentStatic(component, owner, false, self);

            /// <summary>그 Def 타입 하나, 모르면 스키마의 모든 Def 타입.</summary>
            private IEnumerable<DefType> Owners(DefType? type) => type != null ? new[] { type } : (IEnumerable<DefType>)_schema.Types;

            /// <summary>
            /// 후보는 먼저 그 값의 것(실행 오류와 같은 목록), 거기에 없으면 스키마의 컴포넌트·Def 타입 이름 전체다
            /// (붙지 않은 컴포넌트도 맞는 이름이다).
            /// </summary>
            private void Error(string message, AttributeExpr attr, List<string> candidates, bool schemaNames = true)
            {
                var hint = Suggest.Hint(attr.Name, candidates);
                if (hint == null && schemaNames)
                {
                    var all = new List<string>();
                    foreach (var c in _schema.Components) all.Add(c.Name);
                    foreach (var t in _schema.Types) all.Add(t.Name);
                    hint = Suggest.Hint(attr.Name, all);
                }
                _stage!.Error(_mod, Codes.EvlUnknownName, message, attr.Span, hint);
            }
        }
    }
}
