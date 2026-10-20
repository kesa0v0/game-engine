using System;
using System.Collections.Generic;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Schema;
using Kesa.Engine.Script;

namespace Kesa.Engine.Loading
{
    /// <summary>
    /// 함수 본문의 속성 이름 검사 (spec-gaps G228, G108 보강). 본문을 훑으며 값의 타입을 아는 만큼만 기록하고, 타입을 아는 값에
    /// 그런 이름이 아예 없으면 로드 오류다(<c>EVL001</c> + "혹시 …?"). 이름이 맞는지는 실행과 같은 규칙으로 본다(엔티티는
    /// <c>World.EntityAttribute</c>, 컴포넌트는 <c>World.ComponentAttribute</c>, Def 타입 이름 뒤는 <c>World.DefConst</c>).
    /// 타입을 모르면 넘어간다(G207의 가짜 오류 0개). Def의 최종 값을 봐야 <c>Cond</c>·<c>Action</c> 칸에 든 함수를 알므로
    /// ⑤ 링크와 함께 돈다. 책임은 함수를 정의한 모드다.
    /// </summary>
    internal static partial class AttributeChecker
    {
        /// <summary>모드 하나의 함수와 이름 공간 (내장을 덮어썼는지 보려고).</summary>
        internal sealed class ModCode
        {
            public ModCode(string mod, ModFunctions functions, ModScope scope)
            {
                Mod = mod;
                Functions = functions;
                Scope = scope;
            }

            public string Mod { get; }
            public ModFunctions Functions { get; }
            public ModScope Scope { get; }
        }

        public static void Check(IReadOnlyList<ModCode> mods, ResolvedDefs resolved, DefRegistry defs, SchemaSet schema,
            StageDiagnostics stage)
        {
            var contexts = new Contexts();
            foreach (var mod in mods) CollectEntries(mod.Functions, contexts, schema);
            CollectSchema(schema, contexts);
            foreach (var def in resolved.Defs) CollectDef(def, contexts, schema);

            var returns = new ReturnTypes(schema, defs, mods);
            foreach (var mod in mods)
            {
                var globals = returns.Globals(mod.Mod);
                foreach (var function in mod.Functions.Functions)
                {
                    var parameters = new List<KeyValuePair<string, StaticType?>>();
                    var given = contexts.Of(Node(function));
                    for (int i = 0; i < function.Parameters.Count; i++)
                    {
                        var p = function.Parameters[i];
                        var type = p.Annotation != null ? Annotation(p.Annotation, schema) : given != null ? given[i] : null;
                        parameters.Add(new KeyValuePair<string, StaticType?>(p.Name, type ?? StaticType.Unknown));
                    }
                    new Walker(schema, defs, globals, stage, function.Mod, returns: returns).Function(function, parameters);
                }
            }

            CheckDerivedCycles(mods, defs, schema, stage);
        }

        private static object Node(FunctionValue function) => (object?)function.Definition ?? function.Lambda!;

        /// <summary>같은 이름의 내장을 가리는 이름: 모드가 묶은 이름과 이 모드에 보이는 컴포넌트·신호 이름 (G112·G230).</summary>
        private static ModNames Globals(ModCode mod, SchemaSet schema)
        {
            var names = new ModNames();
            names.All.UnionWith(mod.Scope.Names);
            names.All.UnionWith(mod.Functions.TopLevelNames);
            // 모드가 묶지 않은 보이는 컴포넌트·신호 이름은 그 생성자다 (G230)
            foreach (var c in schema.Components)
                if (mod.Scope.IsSchemaName(c.Name) && !names.All.Contains(c.Name)) names.Constructors[c.Name] = "component";
            foreach (var s in schema.Signals)
                if (!EngineSignals.IsEngine(s.Name) && mod.Scope.IsSchemaName(s.Name) && !names.All.Contains(s.Name))
                    names.Constructors[s.Name] = "signal";
            names.All.UnionWith(names.Constructors.Keys);
            return names;
        }

        /// <summary>모드 함수 본문에서 지역 변수가 아닌 이름: 모드가 묶은 것과 보이는 스키마 이름 (G112·G230).</summary>
        private sealed class ModNames
        {
            public HashSet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal);

            /// <summary>생성자인 스키마 이름과 그 종류(<c>component</c>·<c>signal</c>).</summary>
            public SortedDictionary<string, string> Constructors { get; } = new SortedDictionary<string, string>(StringComparer.Ordinal);
        }

        // ---- 문맥: 매개변수 타입을 정해 주는 자리 ----

        /// <summary>함수(정의 노드)마다 매개변수 위치별 타입. 여러 문맥에서 쓰이면 합친다.</summary>
        private sealed class Contexts
        {
            private readonly Dictionary<object, StaticType?[]> _byFunction = new Dictionary<object, StaticType?[]>();

            public StaticType?[]? Of(object node) => _byFunction.TryGetValue(node, out var types) ? types : null;

            public void Add(FunctionValue function, IReadOnlyList<StaticType?> types)
            {
                var node = Node(function);
                if (!_byFunction.TryGetValue(node, out var known))
                {
                    known = new StaticType?[function.Parameters.Count];
                    for (int i = 0; i < known.Length; i++) known[i] = i < types.Count ? types[i] ?? StaticType.Unknown : StaticType.Unknown;
                    _byFunction[node] = known;
                    return;
                }
                for (int i = 0; i < known.Length; i++)
                    known[i] = StaticType.Join(known[i], i < types.Count ? types[i] ?? StaticType.Unknown : StaticType.Unknown);
            }

            /// <summary>매개변수를 이름으로 골라 받는 함수 (핸들러, <c>acts_for</c>).</summary>
            public void AddByName(FunctionValue function, Func<string, StaticType?> byName)
            {
                var types = new List<StaticType?>();
                foreach (var p in function.Parameters) types.Add(byName(p.Name));
                Add(function, types);
            }

            /// <summary>매개변수가 하나인 함수 (<c>Cond["X"]</c>·<c>Action["X"]</c> 칸, 계산할 자리 하나를 받는 함수).</summary>
            public void AddSingle(FunctionValue function, StaticType type)
            {
                if (function.Parameters.Count == 1) Add(function, new[] { type });
            }
        }

        private static void CollectEntries(ModFunctions functions, Contexts contexts, SchemaSet schema)
        {
            foreach (var command in functions.Commands)
            {
                var types = new Dictionary<string, StaticType?>(StringComparer.Ordinal);
                foreach (var p in command.Parameters) types[p.Name] = FromType(p.Type, schema);
                StaticType? ByName(string name) => types.TryGetValue(name, out var t) ? t : null;
                contexts.AddByName(command.Function, ByName);
                if (command.ActsFor != null) contexts.AddByName(command.ActsFor, ByName);
            }
            foreach (var handler in functions.Handlers)
                contexts.AddByName(handler.Function,
                    name => handler.Signal.TryGetField(name, out var field) ? FromType(field.Type, schema) : null);
            foreach (var when in functions.Whens)
            {
                if (when.Subject == null) continue;
                var subject = new EntityType(schema.TryGetType(when.Subject, out var t) ? t : null);
                contexts.AddSingle(when.Function, subject);
                contexts.AddSingle(when.Condition, subject);
            }
        }

        /// <summary>
        /// 스키마의 함수 자리: <c>derived</c> 함수와 <c>field</c>의 함수 기본값은 그 컴포넌트(키 컴포넌트면 그 줄)를 받는다.
        /// 어느 Def 타입의 엔티티인지는 모른다(런타임에 붙는 컴포넌트가 있다). <c>Cond</c> 자리의 함수 기본값은 그 타입의 엔티티를 받는다.
        /// </summary>
        private static void CollectSchema(SchemaSet schema, Contexts contexts)
        {
            foreach (var component in schema.Components)
            {
                var view = new ComponentStatic(component, null, component.IsKeyed);
                foreach (var m in component.Members)
                {
                    if (m.Kind == MemberKind.Derived && m.Spec.Function != null) contexts.AddSingle(m.Spec.Function, view);
                    else if (m.Kind == MemberKind.Field && m.Spec.Default is FunctionValue fn) contexts.AddSingle(fn, view);
                    else Slot(m.Spec.Type, m.Spec.Default, contexts, schema);
                }
            }
            foreach (var type in schema.Types)
            foreach (var m in type.Consts)
                Slot(m.Spec.Type, m.Spec.Default, contexts, schema);
        }

        private static void CollectDef(ResolvedDef def, Contexts contexts, SchemaSet schema)
        {
            foreach (var decl in def.Type.Consts)
                if (def.Consts.TryGet(decl.Name, out var value)) Slot(decl.Spec.Type, value.Value, contexts, schema);
            foreach (var name in def.Components.Keys)
            {
                def.Components.TryGet(name, out var rc);
                Members(rc.Members, rc.Component, contexts, schema);
                foreach (var key in rc.Rows.Keys)
                {
                    rc.Rows.TryGet(key, out var row);
                    Members(row.Members, rc.Component, contexts, schema);
                }
            }
        }

        private static void Members(OrderedMap<ResolvedValue> members, ComponentType component, Contexts contexts, SchemaSet schema)
        {
            foreach (var name in members.Keys)
            {
                members.TryGet(name, out var value);
                if (component.TryGetMember(name, out var member)) Slot(member.Spec.Type, value.Value, contexts, schema);
            }
        }

        /// <summary><c>Cond["X"]</c>·<c>Action["X"]</c> 칸(리스트 안 포함)에 든 함수는 매개변수가 하나면 X의 엔티티를 받는다.</summary>
        private static void Slot(TypeRef type, Value? value, Contexts contexts, SchemaSet schema)
        {
            switch (type)
            {
                case FunctionTypeRef slot when value is FunctionValue fn:
                    contexts.AddSingle(fn, new EntityType(schema.TryGetType(slot.Target.Name, out var target) ? target : null));
                    return;
                case ListTypeRef list when value is ListValue items:
                    foreach (var item in items.Items) Slot(list.Element, item, contexts, schema);
                    return;
                case OptionalTypeRef optional:
                    Slot(optional.Inner, value, contexts, schema);
                    return;
            }
        }

        // ---- 타입 표기 ----

        /// <summary>스키마 타입 표기의 정적 타입. 엔티티(와 그 리스트)만 알고 나머지는 모름이다.</summary>
        internal static StaticType? FromType(TypeRef type, SchemaSet schema)
        {
            switch (type)
            {
                case EntityRefTypeRef entity:
                    return new EntityType(schema.TryGetType(entity.Target.Name, out var t) ? t : null);
                case OptionalTypeRef optional:
                    return FromType(optional.Inner, schema);
                case DefIdTypeRef _:
                    return DefIdStatic.Instance;
                case DictTypeRef dict when dict.Key is DefIdTypeRef:
                    return DefIdMapType.Instance;
                case ListTypeRef list:
                    var element = FromType(list.Element, schema);
                    return element is EntityType || element is DefIdStatic ? new GroupType(element!) : StaticType.Unknown;
            }
            return StaticType.Unknown;
        }

        /// <summary>
        /// 매개변수 어노테이션 (실행하지 않고 모양으로 읽는다): <c>Ref[X]</c>·<c>Ref["X"]</c>, Def 타입 이름 <c>X</c>(그 엔티티,
        /// 스텁의 <c>type Ref[T] = T</c>와 같다), 키 없는 컴포넌트 이름(그 컴포넌트), <c>T | None</c>, <c>list[...]</c>. 그 밖은 모름.
        /// </summary>
        private static StaticType Annotation(Expr expr, SchemaSet schema)
        {
            switch (expr)
            {
                case NameExpr name:
                    if (schema.TryGetType(name.Name, out var type)) return new EntityType(type);
                    if (schema.TryGetComponent(name.Name, out var component) && !component.IsKeyed)
                        return new ComponentStatic(component, null, false);
                    return StaticType.Unknown;
                case StringLiteral s:
                    return Annotation(new NameExpr(s.Value, s.Span), schema);
                case IndexExpr index when index.Target is NameExpr generic && index.Indices.Count == 1:
                    if (generic.Name == "Ref")
                    {
                        string? target = index.Index is NameExpr n ? n.Name : index.Index is StringLiteral l ? l.Value : null;
                        return target != null && schema.TryGetType(target, out var t) ? new EntityType(t) : StaticType.Unknown;
                    }
                    if (generic.Name == "DefId") return DefIdStatic.Instance;
                    if (generic.Name == "list")
                    {
                        var element = Annotation(index.Index, schema);
                        return element is EntityType || element is DefIdStatic ? new GroupType(element) : StaticType.Unknown;
                    }
                    return StaticType.Unknown;
                case IndexExpr dict when dict.Target is NameExpr d && d.Name == "dict" && dict.Indices.Count == 2:
                    return Annotation(dict.Indices[0], schema) is DefIdStatic ? DefIdMapType.Instance : StaticType.Unknown;
                case BinaryExpr union when union.Op == "|":
                    if (union.Right is NoneLiteral) return Annotation(union.Left, schema);
                    if (union.Left is NoneLiteral) return Annotation(union.Right, schema);
                    return StaticType.Unknown;
            }
            return StaticType.Unknown;
        }
    }
}
