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
            private readonly HashSet<string> _globals;
            private readonly StageDiagnostics _stage;
            private readonly string _mod;

            /// <summary>이번 바퀴에 지역 변수마다 대입한 값의 타입 (합친 것).</summary>
            private Dictionary<string, StaticType?> _assigned = new Dictionary<string, StaticType?>(StringComparer.Ordinal);

            /// <summary>마지막 바퀴인가: 이때만 오류를 낸다.</summary>
            private bool _report;

            public Walker(SchemaSet schema, DefRegistry defs, HashSet<string> globals, StageDiagnostics stage, string mod)
            {
                _schema = schema;
                _defs = defs;
                _globals = globals;
                _stage = stage;
                _mod = mod;
            }

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
                if (function.Definition != null) Block(function.Definition.Body, scope);
                else Type(function.Lambda!.Body, scope);
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
                            if (ret.Value != null) Type(ret.Value, scope);
                            break;
                    }
                }
            }

            private static StaticType? Element(StaticType? iterable) =>
                iterable == null ? null : iterable is GroupType group ? group.Element : StaticType.Unknown;

            /// <summary>이름이 지역 변수나 모드의 최상위 이름이라 내장을 가렸는가 (G112).</summary>
            private bool Shadowed(string name, SortedDictionary<string, StaticType?> scope) =>
                scope.ContainsKey(name) || _globals.Contains(name);

            /// <summary>식의 타입. null은 아직 정보가 없다는 뜻이다 (대입을 모으는 중).</summary>
            private StaticType? Type(Expr expr, SortedDictionary<string, StaticType?> scope)
            {
                switch (expr)
                {
                    case NameExpr name:
                        return scope.TryGetValue(name.Name, out var local) ? local : StaticType.Unknown;
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
                        }
                        return StaticType.Unknown;
                    case BinaryExpr bin:
                        Type(bin.Left, scope);
                        Type(bin.Right, scope);
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
                for (int i = candidate != null ? 1 : 0; i < call.Arguments.Count; i++)
                {
                    var arg = call.Arguments[i];
                    if (candidate != null && arg.Name == FunctionChecker.WhereName && arg.Value is LambdaExpr where)
                        Lambda(where, scope, candidate);
                    else
                        Type(arg.Value, scope);
                }

                if (builtin == PureBuiltins.QueryName) return new GroupType(new EntityType(QueriedType(call)));
                if (builtin == PureBuiltins.SpawnName)
                {
                    DefType? type = null;
                    if (call.Arguments.Count > 0 && call.Arguments[0].Value is StringLiteral id && _defs.TryGet(id.Value, out var record))
                        type = record.Type;
                    return new EntityType(type);
                }
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
                }
                return StaticType.Unknown;
            }

            /// <summary>
            /// 스키마의 컴포넌트(붙어 있지 않으면 실행 중 None), Def 타입 이름(그 타입이 아니면 None), 그 타입의 역방향 컬렉션.
            /// Def 타입을 모르면 어느 타입의 역방향 컬렉션이든 받는다.
            /// </summary>
            private StaticType? EntityAttribute(EntityType entity, AttributeExpr attr)
            {
                string name = attr.Name;
                if (_schema.TryGetComponent(name, out var component)) return View(component, entity.Type);
                if (_schema.TryGetType(name, out var defType)) return new DefConstsType(defType);
                foreach (var type in Owners(entity.Type))
                foreach (var inverse in type.Inverses)
                    if (inverse.Name == name)
                        return new GroupType(new EntityType(null));

                if (!_report) return StaticType.Unknown;
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
                if (component.TryGetMember(name, out var member)) return FromType(member.Spec.Type, _schema);
                if (name == "entity") return new EntityType(view.Owner);
                if (name == "key" && view.Row)
                    return component.KeyKind == KeyKind.Entity ? new EntityType(component.Key) : StaticType.Unknown;
                if (_schema.TryGetComponent(name, out var sibling)) return View(sibling, view.Owner);
                if (_schema.TryGetType(name, out var defType)) return new DefConstsType(defType);

                if (!_report) return StaticType.Unknown;
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

                if (!_report) return StaticType.Unknown;
                var names = new List<string>();
                foreach (var decl in consts.Type.Consts) names.Add(decl.Name);
                Error("Def type '" + consts.Type.Name + "' has no const '" + attr.Name + "'", attr, names, schemaNames: false);
                return StaticType.Unknown;
            }

            private static StaticType View(ComponentType component, DefType? owner) =>
                component.IsKeyed ? new RowsType(component, owner) : (StaticType)new ComponentStatic(component, owner, false);

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
                _stage.Error(_mod, Codes.EvlUnknownName, message, attr.Span, hint);
            }
        }
    }
}
