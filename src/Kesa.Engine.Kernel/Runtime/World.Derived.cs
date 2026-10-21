using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Host;
using Kesa.Engine.Loading;
using Kesa.Engine.Schema;
using Kesa.Engine.Script;

namespace Kesa.Engine.Runtime
{
    /// <summary>
    /// <c>derived</c> 읽기 (spec S4, spec-gaps G100·G103·G105). 저장하지 않고 읽을 때마다 순수 함수로 계산한다.
    /// 함수는 계산할 컴포넌트(키 컴포넌트면 줄) 하나를 받고, 그 컴포넌트의 멤버와 같은 엔티티의 형제 컴포넌트,
    /// Ref를 따라간 다른 엔티티를 읽는다.
    /// </summary>
    public sealed partial class World
    {
        /// <summary><c>derived</c> 안에서 다른 <c>derived</c>를 읽는 연쇄의 깊이 상한 (스택이 넘치기 전에 오류로 알린다).</summary>
        public const int MaxDerivedDepth = 64;

        private readonly IReadOnlyDictionary<string, ModScope> _scopes;
        private readonly int _stepBudget;

        /// <summary>행동 실행(커맨드 본문, 핸들러, Flow 이어 하기) 하나의 단계 예산 (G224).</summary>
        private readonly int _actionStepBudget;

        /// <summary>
        /// 지금 만드는 결과(제출, 답, 핸들러 하나)에 붙일 경고 (G225). 결과 단위가 열려 있을 때만 null이 아니다: 결과가 없는 실행
        /// (사전 검사만, <c>choose</c> 나열)의 경고는 버린다.
        /// </summary>
        private List<Diagnostic>? _warnings;

        /// <summary>결과 단위를 연다. 돌려준 값은 <see cref="CloseWarnings"/>에 넘긴다(안쪽 결과 단위가 바깥 것을 섞지 않게).</summary>
        private List<Diagnostic>? OpenWarnings()
        {
            var outer = _warnings;
            _warnings = new List<Diagnostic>();
            return outer;
        }

        /// <summary><paramref name="run"/>을 결과 단위 하나로 돌리고 그동안 모은 경고를 <paramref name="warnings"/>로 준다.</summary>
        private T WithWarnings<T>(Func<T> run, out IReadOnlyList<Diagnostic> warnings)
        {
            var outer = OpenWarnings();
            try
            {
                return run();
            }
            finally
            {
                warnings = CloseWarnings(outer);
            }
        }

        /// <summary>결과 단위를 닫고 그동안 모은 경고를 돌려준다.</summary>
        private IReadOnlyList<Diagnostic> CloseWarnings(List<Diagnostic>? outer)
        {
            var mine = _warnings!;
            _warnings = outer;
            return mine.Count == 0 ? (IReadOnlyList<Diagnostic>)Array.Empty<Diagnostic>() : mine;
        }

        /// <summary>
        /// 가장 바깥 행동 실행의 계량기를 닫는다. 읽기 예산보다 많이 썼으면 실패시키지 않고 경고를 남긴다 (G225): 오류 상한(행동 예산)에
        /// 닿기 전에 무거운 곳을 알게 한다. 넘을 때마다 남기고, 걸러 보여 줄지는 게임이 정한다.
        /// </summary>
        /// <returns>경고를 남길 만큼 썼으면 쓴 단계 수(<see cref="WarnHeavy"/>에 넘긴다), 아니면 0. 문구는 그때만 만든다.</returns>
        private int CloseActionMeter()
        {
            var meter = _meter!;
            _meter = null;
            return meter.Used > _stepBudget && _warnings != null ? meter.Used : 0;
        }

        private void WarnHeavy(int used, string label, SourceSpan span)
        {
            _warnings!.Add(new Diagnostic(Codes.RunHeavy, Severity.Warning,
                label + " used " + used.ToString(CultureInfo.InvariantCulture) + " evaluation steps, more than the read budget of " +
                _stepBudget.ToString(CultureInfo.InvariantCulture) + " (it fails at " +
                _actionStepBudget.ToString(CultureInfo.InvariantCulture) + ")",
                null, span, "spread the work over ticks, or move it into a host function (spec-gaps G225)"));
        }
        private PureEvaluator? _evaluator;

        /// <summary>지금 계산 중인 <c>derived</c>·기본값들 (바깥부터). 같은 값을 다시 읽으면 순환이다 (G100).</summary>
        private readonly List<Computing> _computing = new List<Computing>();

        /// <summary>
        /// 계산 중인 값 하나. 진단 글(<see cref="Label"/>)은 순환·오류일 때만 만든다: 계산마다 만들면 읽기가 느려진다(규모 벤치).
        /// </summary>
        private readonly struct Computing
        {
            public Computing(EntityRecord entity, ComponentType component, RowKey? row, MemberDecl member, bool isDefault)
            {
                Entity = entity;
                Component = component;
                Row = row;
                Member = member;
                IsDefault = isDefault;
            }

            public EntityRecord Entity { get; }
            public ComponentType Component { get; }
            public RowKey? Row { get; }
            public MemberDecl Member { get; }

            /// <summary>생성 때의 함수 기본값 계산이면 참 (같은 칸의 <c>derived</c>와 다른 것).</summary>
            public bool IsDefault { get; }

            public bool Same(Computing other) =>
                Entity.Id == other.Entity.Id && ReferenceEquals(Component, other.Component) &&
                ReferenceEquals(Member, other.Member) && IsDefault == other.IsDefault &&
                (Row == null ? other.Row == null : other.Row != null && Row.Value.Same(other.Row.Value));

            public string Label => IsDefault
                ? "default of " + Component.Name + "." + Member.Name + " of " + new EntityId(Entity.Id)
                : DerivedLabel(Entity, Component, Row, Member);
        }

        private int IndexOfComputing(Computing value)
        {
            for (int i = 0; i < _computing.Count; i++)
                if (_computing[i].Same(value)) return i;
            return -1;
        }

        private List<string> ComputingLabels(int from)
        {
            var labels = new List<string>();
            for (int i = from; i < _computing.Count; i++) labels.Add(_computing[i].Label);
            return labels;
        }

        /// <summary>지금 진행 중인 최상위 함수 실행의 단계 계량기. 그 안의 중첩 실행이 같은 예산을 나눠 쓴다 (G101).</summary>
        private StepMeter? _meter;

        /// <summary><see cref="RunFunction"/>이 가장 바깥 실행마다 비워서 다시 쓰는 계량기.</summary>
        private StepMeter? _derivedMeterStore;

        private StepMeter _derivedMeter => _derivedMeterStore ??= new StepMeter(_stepBudget);

        private PureEvaluator Evaluator =>
            _evaluator ??= new PureEvaluator(new WorldGlobals(this), RuntimeBuiltins(), _stepBudget) { ParameterType = CommandParameterType };

        private Dictionary<FunctionValue, CommandDecl>? _commandsByFunction;

        private TypeRef? CommandParameterType(FunctionValue function, string parameter)
        {
            if (_commandsByFunction == null)
            {
                _commandsByFunction = new Dictionary<FunctionValue, CommandDecl>();
                foreach (var decl in _commands.Values) _commandsByFunction[decl.Function] = decl;
                foreach (var decl in _uiActions.Values) _commandsByFunction[decl.Function] = decl;
            }
            if (!_commandsByFunction.TryGetValue(function, out var command)) return null;
            foreach (var p in command.Parameters)
                if (p.Name == parameter) return p.Type;
            return null;
        }

        /// <summary>
        /// 함수 본문의 전역 이름: 모드가 묶은 이름, 그다음 그 모드에 보이는 컴포넌트·신호 생성자 (G230). 내장은 그 뒤다
        /// (<see cref="PureEvaluator"/>가 본다). 로드 때의 이름 검사(<c>ModScope.TryLookup</c>)와 같은 차례다.
        /// </summary>
        private sealed class WorldGlobals : IScriptGlobals
        {
            private readonly World _world;
            private readonly SortedDictionary<string, KeyValuePair<string, Value>> _schemaNames =
                new SortedDictionary<string, KeyValuePair<string, Value>>(StringComparer.Ordinal);

            /// <summary>모드 정보가 없는 월드(테스트용 생성자)는 모든 스키마 이름이 보인다.</summary>
            private readonly ModVisibility? _visibility;

            public WorldGlobals(World world)
            {
                _world = world;
                world.AddComponentConstructors(_schemaNames);
                world.AddSignalConstructors(_schemaNames);
                if (world._mods != null) _visibility = new ModVisibility(world._mods);
            }

            /// <summary>그 이름의 컴포넌트·신호 생성자 (보이는 범위를 따지지 않는다: 저장된 값을 되살릴 때).</summary>
            public Value? SchemaValue(string name) => _schemaNames.TryGetValue(name, out var owned) ? owned.Value : null;

            public Value? Lookup(string mod, string name)
            {
                if (_world._scopes.TryGetValue(mod, out var scope) && scope.TryGet(name, out var bound, out _)) return bound;
                if (!_schemaNames.TryGetValue(name, out var owned)) return null;
                return _visibility == null || _visibility.CanSee(mod, owned.Key) ? owned.Value : null;
            }

            public IEnumerable<string> Names(string mod)
            {
                if (_world._scopes.TryGetValue(mod, out var scope))
                    foreach (var name in scope.Names) yield return name;
                foreach (var pair in _schemaNames)
                    if (_visibility == null || _visibility.CanSee(mod, pair.Value.Key)) yield return pair.Key;
            }
        }

        /// <summary>
        /// 함수를 실행한다. 가장 바깥 실행(<c>derived</c> 읽기나 생성 순간의 기본값 하나)이 예산 하나를 열고,
        /// 그 안에서 읽힌 다른 <c>derived</c>는 남은 단계를 이어서 쓴다 (G101: 최상위 계산 하나마다 예산 하나).
        /// </summary>
        private Value RunFunction(FunctionValue function, Value self) => RunFunction(function, new[] { self });

        private Value RunFunction(FunctionValue function, Value[] arguments)
        {
            var span = function.Definition?.Span ?? function.Lambda!.Span;
            bool outermost = _meter == null;
            if (outermost)
            {
                _derivedMeter.Used = 0; // 읽기마다 새로 만들지 않는다. 가장 바깥 실행은 한 번에 하나다
                _meter = _derivedMeter;
            }
            try
            {
                return Evaluator.Run(function, arguments, span, _meter);
            }
            finally
            {
                if (outermost) _meter = null;
            }
        }

        /// <summary><c>text()</c>의 런타임 인자 (G267): 엔티티는 그 Def id(이미 없는 엔티티는 빈 문자열), 숨김 값은 빈 문자열, 그 밖은 받지 않는다.</summary>
        private string? TextArgument(Value value) =>
            value is IEntityReference e ? (IsAlive(e.EntityId) ? DefOf(e.EntityId) : "")
            : value is HiddenValue ? ""
            : null;

        private IReadOnlyDictionary<string, Value> RuntimeBuiltins()
        {
            var table = PureBuiltins.Create();
            table.Add(PureBuiltins.ContribsName, new PureBuiltin(PureBuiltins.ContribsName, ContribsBuiltin));
            table.Add(PureBuiltins.NowName, new PureBuiltin(PureBuiltins.NowName, NowBuiltin));
            table.Add(PureBuiltins.QueryName, new PureBuiltin(PureBuiltins.QueryName, QueryBuiltin));
            table.Add(PureBuiltins.ControllerName, new PureBuiltin(PureBuiltins.ControllerName, ControllerBuiltin));
            // text(): 엔티티 인자는 부르는 때의 Def를 적어 둔다 (G267)
            table[PureBuiltins.TextName] = new PureBuiltin(PureBuiltins.TextName, (p, k) => PureBuiltins.MakeText(p, k, TextArgument));
            table.Add(PureBuiltins.DefIdName,
                new PureBuiltin(PureBuiltins.DefIdName, (p, k) => DefIdBuiltin(_evaluator?.ScriptMod ?? "", p, k))
                {
                    CallerBody = DefIdBuiltin,
                });
            table.Add(PureBuiltins.DefDataName,
                new PureBuiltin(PureBuiltins.DefDataName, (p, k) => DefDataBuiltin(_evaluator?.ScriptMod ?? "", p, k))
                {
                    CallerBody = DefDataBuiltin,
                });
            AddStructureBuiltins(table);
            AddContributionBuiltins(table);
            AddSignalBuiltins(table);
            AddFlowBuiltins(table);
            AddRandomBuiltins(table);
            AddUiBuiltins(table);
            AddHostBuiltins(table);
            return table;
        }

        // ---- 계산 ----

        /// <summary>월드 API에서 부르는 <c>derived</c> 읽기. 스크립트 오류는 위치와 호출 흔적이 붙은 <see cref="WorldException"/>이 된다.</summary>
        private Value ReadDerivedTop(EntityRecord entity, ComponentType component, RowKey? row, MemberDecl member)
        {
            try
            {
                return ComputeDerived(entity, component, row, member);
            }
            catch (ScriptRuntimeException e)
            {
                throw ToWorldException(e, DerivedLabel(entity, component, row, member));
            }
        }

        /// <summary>흔적에서 중첩 <c>derived</c> 읽기의 경계 줄.</summary>
        private const string ReadingPrefix = "reading ";

        private static WorldException ToWorldException(ScriptRuntimeException e, string computing)
        {
            var text = new StringBuilder();
            text.Append("computing ").Append(computing).Append(": ").Append(e.Message);
            if (e.Span.Line > 0) text.Append(" (at ").Append(e.Span).Append(')');
            foreach (var step in e.Trace)
                text.Append(step.StartsWith(ReadingPrefix, StringComparison.Ordinal) ? "\n  " : "\n  in ").Append(step);
            return new WorldException(e.Code, text.ToString(), e.Hint, e.InnerException);
        }

        private static string DerivedLabel(EntityRecord entity, ComponentType component, RowKey? row, MemberDecl member) =>
            component.Name + "." + member.Name + (row != null ? "[" + row.Value + "]" : "") + " of " + new EntityId(entity.Id);

        private Value ComputeDerived(EntityRecord entity, ComponentType component, RowKey? row, MemberDecl member)
        {
            var computing = new Computing(entity, component, row, member, isDefault: false);
            int index = IndexOfComputing(computing);
            if (index >= 0)
            {
                string label = computing.Label;
                var chain = ComputingLabels(index);
                chain.Add(label);
                throw new ScriptRuntimeException(Codes.RunCycle,
                    label + " reads itself: " + string.Join(" -> ", chain),
                    "a derived value cannot depend on itself; split the formula in two (schema-design 3.3.1)");
            }
            if (_computing.Count >= MaxDerivedDepth)
            {
                throw new ScriptRuntimeException(Codes.RunCycle,
                    "derived values are nested more than " + MaxDerivedDepth + " deep while reading " + computing.Label + ": " +
                    string.Join(" -> ", ComputingLabels(0)),
                    "a very long chain of derived reads is treated like a cycle");
            }

            var function = member.Spec.Function!;
            _computing.Add(computing);
            try
            {
                var self = new ComponentView(this, new EntityId(entity.Id), component, row);
                var result = OutsideAct(() => RunFunction(function, self));
                return CheckDerivedResult(member.Display, member, result);
            }
            catch (ScriptRuntimeException e) when (_computing.Count > 1)
            {
                // 다른 계산 안에서 읽힌 값이면 흔적에 경계를 남긴다. 바깥 함수의 프레임은 바깥 실행이 덧붙인다
                e.AddTrace(ReadingPrefix + computing.Label);
                throw;
            }
            finally
            {
                _computing.RemoveAt(_computing.Count - 1);
            }
        }

        /// <summary>반환 값이 선언한 타입과 맞는지 본다. <c>int</c>는 <c>fixed</c>로 올리지만 그 반대는 <c>floor()</c> 등으로 명시해야 한다.</summary>
        private Value CheckDerivedResult(string display, MemberDecl member, Value value)
        {
            var type = member.Spec.Type;
            var plain = ToPlain(display, value);
            if (type is EntityRefTypeRef refType)
            {
                if (plain is EntityValue ev && _entities.TryGetValue(ev.Id.Value, out var target) &&
                    target.Def.Type.Name == refType.Target.Name)
                    return plain;
                throw new ScriptRuntimeException(Codes.RunType,
                    "'" + display + "' is declared " + type.Display + ", but the function returned " + plain.KindName);
            }

            var normalized = Normalize(type, plain);
            string? problem = PatchOperationCheck.Mismatch(type, normalized);
            if (problem != null)
            {
                throw new ScriptRuntimeException(Codes.RunType,
                    "'" + display + "' is declared " + type.Display + ": " + problem,
                    plain is FixedValue && ReferenceEquals(type, PrimitiveTypeRef.Int)
                        ? "turn the fixed value into an int with floor(), ceil() or round() (spec-gaps G95)"
                        : null);
            }
            return normalized;
        }

        /// <summary>함수 본문에서 보이던 엔티티 값을 저장 가능한 값(<see cref="EntityValue"/>)으로 바꾼다. 그 밖의 런타임 객체는 돌려줄 수 없다.</summary>
        private static Value ToPlain(string display, Value value)
        {
            switch (value)
            {
                case IEntityReference r: return new EntityValue(r.EntityId);
                case ListValue list:
                    var items = new List<Value>(list.Items.Count);
                    foreach (var item in list.Items) items.Add(ToPlain(display, item));
                    return new ListValue(items);
                case DictValue dict:
                    var entries = new List<DictItem>(dict.Items.Count);
                    foreach (var item in dict.Items) entries.Add(new DictItem(item.Key, ToPlain(display, item.Value), item.Span));
                    return new DictValue(entries);
                case ComponentViewBase _:
                    throw new ScriptRuntimeException(Codes.RunType,
                        "'" + display + "' returned a component, which cannot be a value", "return one of its members");
            }
            return value;
        }

        /// <summary>월드의 값을 함수 본문에서 보는 값으로: 엔티티는 따라갈 수 있는 모양으로 바꾼다.</summary>
        private Value ToScript(Value value) => value is EntityValue e ? new EntityView(this, e.Id) : value;

        /// <summary>월드 API가 던진 <see cref="WorldException"/>을 함수 본문 안의 오류로 바꾼다 (코드는 그대로).</summary>
        private static T Guard<T>(Func<T> read)
        {
            try
            {
                return read();
            }
            catch (WorldException e)
            {
                throw new ScriptRuntimeException(e.Code, e.Message, e.Hint);
            }
        }

        /// <summary>
        /// 생성 순간(G89)에 엔티티가 있어야 계산되는 기본값들을 선언 순서대로 계산해 저장한다.
        /// 한 칸을 저장한 뒤에 다음 칸을 계산하므로 앞 칸은 뒤 칸이 읽을 수 있고, 뒤 칸을 읽으면 "아직 값이 없다" 오류다.
        /// </summary>
        private void FinishPending(EntityRecord entity, List<PendingDefault> pending)
        {
            foreach (var item in pending)
            {
                var member = item.Member;
                string display = member.Display;
                Value raw;
                try
                {
                    raw = item.Function != null
                        ? RunDefault(entity, item)
                        : ReadMember(entity, item.Component, item.Row, item.Source!);
                }
                catch (ScriptRuntimeException e)
                {
                    throw ToWorldException(e, "the default of " + display + " (" + item.Where + ")");
                }
                Value stored;
                try
                {
                    stored = CheckWrite(display, member, ToPlain(display, raw));
                }
                catch (WorldException e)
                {
                    throw new WorldException(e.Code, "the default of " + display + " (" + item.Where + "): " + e.Message,
                        e.Hint ?? (raw is FixedValue && ReferenceEquals(member.Spec.Type, PrimitiveTypeRef.Int)
                            ? "turn the fixed value into an int with floor(), ceil() or round() (spec-gaps G95)"
                            : null));
                }
                if (item.Row != null)
                {
                    Assign(entity.Id, item.Component, entity.RowsOf(item.Component).Find(item.Row.Value)!.Values, member, stored);
                    continue;
                }
                if (member.Kind == MemberKind.Ref) Reindex(member, entity.Id, entity.Values, stored);
                Assign(entity.Id, item.Component, entity.Values, member, stored);
            }
        }

        private Value RunDefault(EntityRecord entity, PendingDefault item)
        {
            var function = item.Function!;
            _computing.Add(new Computing(entity, item.Component, item.Row, item.Member, isDefault: true));
            try
            {
                var self = new ComponentView(this, new EntityId(entity.Id), item.Component, item.Row);
                return RunFunction(function, self);
            }
            finally
            {
                _computing.RemoveAt(_computing.Count - 1);
            }
        }

        /// <summary>컴포넌트(줄)의 멤버 하나를 읽는다. <c>derived</c>면 계산한다.</summary>
        private Value ReadMember(EntityRecord entity, ComponentType component, RowKey? row, MemberDecl member)
        {
            // 보이는 derived는 숨긴 칸으로 계산돼도 보인다 (G199): 계산은 실제 상태로
            if (member.Kind == MemberKind.Derived)
                return _viewer == null ? ComputeDerived(entity, component, row, member)
                    : Unviewed(() => ComputeDerived(entity, component, row, member));
            if (row == null) return Read(null, component, member, DefMembers(entity, component), entity.Values);
            if (!FindRow(entity, component, row.Value, out var defRow, out var record))
                throw NoRow(new EntityId(entity.Id), component, row.Value);
            return Read(row, component, member, defRow?.Members, record?.Values ?? NoValues);
        }

        // ---- 함수 본문이 보는 값 ----

        private abstract class ComponentViewBase : Value
        {
        }

        /// <summary>엔티티. 컴포넌트 이름으로 그 컴포넌트를, 역방향 이름으로 그 컬렉션을 읽는다 (spec S3).</summary>
        // ---- 속성 자리의 캐시 (AttributeExpr.Cache, 규모 벤치) ----

        /// <summary><c>u.Food</c>: 그 이름이 이 스키마의 컴포넌트다.</summary>
        private sealed class ComponentSite
        {
            public ComponentSite(SchemaSet schema, ComponentType component)
            {
                Schema = schema;
                Component = component;
            }

            public SchemaSet Schema { get; }
            public ComponentType Component { get; }
        }

        /// <summary><c>c.amount</c>: 이 컴포넌트의 멤버이고, 쓸 때의 핸들도 함께 둔다.</summary>
        private sealed class MemberSite
        {
            public MemberSite(ComponentType component, MemberDecl member)
            {
                Component = component;
                Member = member;
                Handle = new MemberHandle(component, null, member);
            }

            public ComponentType Component { get; }
            public MemberDecl Member { get; }
            public MemberHandle Handle { get; }
        }

        /// <summary>엔티티의 속성을 자리 캐시로. 컴포넌트 이름이 아니면(Def 타입, 역방향 컬렉션) 캐시 없이 이름으로 찾는다.</summary>
        private Value EntityAttribute(EntityId id, AttributeExpr site)
        {
            if (site.Cache is ComponentSite cached && ReferenceEquals(cached.Schema, _schema))
                return SiblingOrNone(Record(id, cached.Component.Name), cached.Component);
            if (_schema.TryGetComponent(site.Name, out var component)) site.Cache = new ComponentSite(_schema, component);
            return EntityAttribute(id, site.Name);
        }

        /// <summary>컴포넌트의 속성을 자리 캐시로. 멤버가 아니면(<c>entity</c>, <c>key</c>, 형제) 이름으로 찾는다.</summary>
        private Value ComponentAttribute(ComponentView view, AttributeExpr site)
        {
            var member = MemberAt(view.Component, site);
            if (member == null) return ComponentAttribute(view, site.Name);
            return ToScript(ViewFiltered(ReadMemberTraced(Record(view.Id, view.Component.Name), view.Component, view.Row, member.Member)));
        }

        private void WriteMember(ComponentView view, AttributeExpr site, Value value)
        {
            var member = MemberAt(view.Component, site);
            if (member == null)
            {
                WriteMember(view, site.Name, value);
                return;
            }
            WriteMember(view, member.Handle, value);
        }

        /// <summary>자리가 이 컴포넌트의 멤버를 가리키면 그 캐시. 컴포넌트 객체가 같으면 스키마도 같다.</summary>
        private static MemberSite? MemberAt(ComponentType component, AttributeExpr site)
        {
            if (site.Cache is MemberSite cached && ReferenceEquals(cached.Component, component)) return cached;
            if (!component.TryGetMember(site.Name, out var member)) return null;
            var found = new MemberSite(component, member);
            site.Cache = found;
            return found;
        }

        private sealed class EntityView : Value, IRuntimeAttributes, ISiteAttributes, IValueEquality, IEntityReference
        {
            private readonly World _world;

            public EntityView(World world, EntityId id)
            {
                _world = world;
                Id = id;
            }

            public EntityId Id { get; }
            public override string KindName => "entity";
            EntityId IEntityReference.EntityId => Id;

            public bool ValueEquals(Value other) => other is IEntityReference r && r.EntityId == Id;

            public Value GetAttribute(string name) => Guard(() => _world.EntityAttribute(Id, name));

            // 자주 도는 길이라 Guard의 람다(할당)를 쓰지 않는다
            public Value GetAttribute(AttributeExpr site)
            {
                try
                {
                    return _world.EntityAttribute(Id, site);
                }
                catch (WorldException e)
                {
                    throw new ScriptRuntimeException(e.Code, e.Message, e.Hint);
                }
            }
        }

        /// <summary>컴포넌트 하나(키 컴포넌트면 줄 하나). 자기 멤버, 형제 컴포넌트, <c>entity</c>, <c>key</c>를 읽는다 (G103).</summary>
        private sealed class ComponentView : ComponentViewBase, IRuntimeAttributes, IRuntimeAssignable, ISiteAttributes, ISiteAssignable,
            IRuntimeSlots, IValueEquality
        {
            /// <summary>같은 엔티티의 같은 컴포넌트(같은 줄)면 같다 (G142).</summary>
            public bool ValueEquals(Value other) =>
                other is ComponentView v && v._id == _id && v.Component == Component && SameRow(v._row, _row);

            private readonly World _world;
            private readonly EntityId _id;
            private readonly RowKey? _row;

            public ComponentView(World world, EntityId id, ComponentType component, RowKey? row)
            {
                _world = world;
                _id = id;
                Component = component;
                _row = row;
            }

            public ComponentType Component { get; }
            public EntityId Id => _id;
            public RowKey? Row => _row;
            public override string KindName => Component.Name;

            public Value GetAttribute(string name) => Guard(() => _world.ComponentAttribute(this, name));

            public void SetAttribute(string name, Value value) => Guard(() => _world.WriteMember(this, name, value));

            public Value GetAttribute(AttributeExpr site)
            {
                try
                {
                    return _world.ComponentAttribute(this, site);
                }
                catch (WorldException e)
                {
                    throw new ScriptRuntimeException(e.Code, e.Message, e.Hint);
                }
            }

            public void SetAttribute(AttributeExpr site, Value value)
            {
                try
                {
                    _world.WriteMember(this, site, value);
                }
                catch (WorldException e)
                {
                    throw new ScriptRuntimeException(e.Code, e.Message, e.Hint);
                }
            }

            public Value ReadSlot(string name) => Guard(() => _world.ReadSlot(this, name));

            public string SlotName(string name) =>
                _id + "." + Component.Name + (_row == null ? "" : "[" + _row.Value + "]") + "." + name;
        }

        /// <summary>
        /// <c>cost</c>·<c>pay</c>가 확인하고 빼는 칸 (spec-gaps G186): <c>int</c>·<c>fixed</c>인 <c>field</c>만. 속성 이름은 로드 때
        /// 보지 않으므로(G108) 여기서 본다.
        /// </summary>
        private Value ReadSlot(ComponentView view, string name)
        {
            var component = view.Component;
            if (!component.TryGetMember(name, out var member))
            {
                var names = new List<string>();
                foreach (var m in component.Members) names.Add(m.Name);
                throw new WorldException(Codes.RunUnknownName, "'" + component.Name + "' has no member '" + name + "'",
                    Suggest.Hint(name, names) ?? "cost() and pay() take a member of a component");
            }
            var type = member.Spec.Type;
            bool number = ReferenceEquals(type, PrimitiveTypeRef.Int) || ReferenceEquals(type, PrimitiveTypeRef.Fixed);
            if (member.Kind != MemberKind.Field || !number)
            {
                string what = member.Kind == MemberKind.Field ? "a " + type.Display + " field" : "a " + member.Kind.ToString().ToLowerInvariant();
                throw new WorldException(Codes.RunType,
                    "cost() and pay() need an int or fixed field, but '" + component.Name + "." + name + "' is " + what,
                    "they take the amount out of a stored number (spec-gaps G186)");
            }
            return ComponentAttribute(view, name);
        }

        /// <summary>
        /// 커맨드 본문의 쓰기 <c>u.Moves.left = 값</c> (G117·G132). 키 컴포넌트면 그 줄에 쓴다(있는 줄만, 줄 뷰가 있으면 줄이 있다).
        /// 검사는 호스트 API와 같다: <c>field</c>·Ref만, 타입·범위 (G92).
        /// </summary>
        private bool WriteMember(ComponentView view, string name, Value value)
        {
            var component = view.Component;
            if (!component.TryGetMember(name, out var member))
            {
                var names = new List<string>();
                foreach (var m in component.Members) names.Add(m.Name);
                throw new WorldException(Codes.RunUnknownName, "'" + component.Name + "' has no member '" + name + "'",
                    Suggest.Hint(name, names) ?? "only members of a component can be written");
            }
            WriteMember(view, new MemberHandle(component, null, member), value);
            return true;
        }

        private void WriteMember(ComponentView view, MemberHandle handle, Value value)
        {
            var plain = ToPlain(handle.Display, value);
            if (view.Row == null) Set(view.Id, handle, plain);
            else SetRow(view.Id, handle, view.Row.Value, plain);
        }

        /// <summary>키 컴포넌트 전체: 키로 줄 하나를 꼬집는다 (<c>h.Skill.Medicine</c>, <c>h.Skill[s]</c>, <c>for s in h.Skill</c>).</summary>
        /// <remarks>
        /// 파이썬 <c>dict</c>처럼 <c>len</c>은 줄 수, <c>k in h.Skill</c>은 그 키의 줄이 있는가, 비면 거짓이다 (G104).
        /// 컴포넌트가 붙어 있는지는 <c>h.Skill is not None</c>으로 따로 묻는다.
        /// </remarks>
        private sealed class RowsView : ComponentViewBase, IRuntimeAttributes, IRuntimeIndexable, IRuntimeLiveCollection, IValueEquality
        {
            /// <summary>같은 엔티티의 같은 키 컴포넌트면 같다 (G142).</summary>
            public bool ValueEquals(Value other) => other is RowsView v && v._id == _id && v._component == _component;

            private readonly World _world;
            private readonly EntityId _id;
            private readonly ComponentType _component;

            public RowsView(World world, EntityId id, ComponentType component)
            {
                _world = world;
                _id = id;
                _component = component;
            }

            public EntityId Id => _id;
            public ComponentType Component => _component;
            public override string KindName => _component.Name + " rows";

            public Value GetAttribute(string name) =>
                Guard(() => _world.RowOrNone(_id, _component, RowKey.Def(_world.ScriptDefId(name))));

            public Value GetItem(Value key) => Guard(() => _world.RowOrNone(_id, _component, KeyOf(key)));

            public IReadOnlyList<Value> Iterate() => Guard(() => _world.RowKeyValues(_id, _component));

            public int Size() => Iterate().Count;

            /// <summary>그 키의 줄이 있는가. 없는 Def id는 <c>h.Skill[k]</c>처럼 "혹시 …?" 오류다 (오타가 조용히 False가 되지 않게).</summary>
            public bool ContainsKey(Value key) => Guard(() => _world.HasRowKey(_id, _component, KeyOf(key)));

            /// <summary>반복 도중 그 줄이 떨어졌거나 엔티티가 죽었다 (G88·G194).</summary>
            public bool IsGone(Value key) =>
                !_world.IsAlive(_id) || (key is IEntityReference r && !_world.IsAlive(r.EntityId)) ||
                !Guard(() => _world.HasRowKey(_id, _component, KeyOf(key)));

            private RowKey KeyOf(Value key)
            {
                switch (key)
                {
                    case StrValue s: return RowKey.Def(_world.ScriptDefId(s.Value));
                    case IEntityReference r: return RowKey.Entity(r.EntityId);
                    case IntValue n: return RowKey.Int(n.Value);
                }
                throw new ScriptRuntimeException(Codes.RunType,
                    "'" + _component.Name + "' rows are keyed by " + _component.KeyDisplay + ", not " + key.KindName);
            }
        }

        private Value EntityAttribute(EntityId id, string name)
        {
            var entity = Record(id, AliveRead);
            if (_schema.TryGetComponent(name, out var component))
            {
                Read(id.Value, component.Name);
                return SiblingOrNone(entity, component);
            }
            if (_schema.TryGetType(name, out var defType)) return DefOrNone(entity, defType);
            if (name == "defid") return new StrValue(entity.Def.Id); // 이 엔티티의 Def 전체 이름 (G293)

            // 역방향 컬렉션은 Inverse가 그 이름으로 기록한다
            var candidates = new List<string> { entity.Def.Type.Name };
            foreach (var c in ComponentsOf(entity)) candidates.Add(c.Name);
            foreach (var inverse in entity.Def.Type.Inverses)
            {
                if (inverse.Name != name)
                {
                    candidates.Add(inverse.Name);
                    continue;
                }
                var handle = new MemberHandle(inverse.Source, null, inverse.Member);
                // 가리키는 쪽이나 그 Ref가 든 컴포넌트가 안 보이면 빠진다 (G198)
                return new EntityGroup(this, ViewFiltered(Inverse(id, handle), inverse.Source.Name), inverse.Name);
            }
            throw new ScriptRuntimeException(Codes.RunScript,
                id + " (" + entity.Def.Id + ") has no component or collection '" + name + "'", Suggest.Hint(name, candidates));
        }

        private Value SiblingOrNone(EntityRecord entity, ComponentType component)
        {
            // Def가 가진 컴포넌트는 늘 있고, 런타임에 붙는 컴포넌트는 없을 수 있다 (None)
            if (!Has(entity, component)) return NoneValue.Instance;
            if (_viewer != null && HiddenFromViewer(entity.Id, component.Name)) return NoneValue.Instance; // 못 보는 컴포넌트는 붙지 않은 것 (G198)
            var id = new EntityId(entity.Id);
            if (component.IsKeyed) return new RowsView(this, id, component);
            return new ComponentView(this, id, component, null);
        }

        private Value ComponentAttribute(ComponentView view, string name)
        {
            var entity = Record(view.Id, AliveRead);
            var component = view.Component;
            if (component.TryGetMember(name, out var member))
            {
                Read(view.Id.Value, component.Name);
                return ToScript(ViewFiltered(ReadMemberTraced(entity, component, view.Row, member)));
            }
            if (name == "entity") return new EntityView(this, view.Id);
            if (name == "key" && view.Row != null)
                return view.Row.Value.IsEntity ? new EntityView(this, new EntityId(view.Row.Value.EntityKey))
                    : view.Row.Value.IsInt ? new IntValue(view.Row.Value.Number)
                    : (Value)new StrValue(view.Row.Value.DefKey!);
            if (_schema.TryGetComponent(name, out var sibling))
            {
                Read(view.Id.Value, sibling.Name);
                return SiblingOrNone(entity, sibling);
            }
            if (_schema.TryGetType(name, out var defType)) return DefOrNone(entity, defType);

            var candidates = new List<string> { "entity", entity.Def.Type.Name };
            if (view.Row != null) candidates.Add("key");
            foreach (var m in component.Members) candidates.Add(m.Name);
            foreach (var c in ComponentsOf(entity)) candidates.Add(c.Name);
            throw new ScriptRuntimeException(Codes.RunScript,
                "'" + component.Name + "' has no member, sibling component or property '" + name + "'",
                Suggest.Hint(name, candidates));
        }

        /// <summary>키로 꼬집은 줄. 줄이 없으면 <c>None</c>이다 (<c>keys="listed"</c>, spec S4).</summary>
        private Value RowOrNone(EntityId id, ComponentType component, RowKey key)
        {
            var entity = Record(id, component.Name);
            HiddenKey(key);
            if (!FindRow(entity, component, key, out _, out _)) return NoneValue.Instance;
            return new ComponentView(this, id, component, key);
        }

        private bool HasRowKey(EntityId id, ComponentType component, RowKey key)
        {
            var entity = Record(id, component.Name);
            HiddenKey(key);
            return FindRow(entity, component, key, out _, out _);
        }

        /// <summary>보는 쪽으로 부르는 중이면 못 보는 엔티티 키는 죽은 키와 같다 (G198·G248).</summary>
        private void HiddenKey(RowKey key)
        {
            if (_viewer != null && key.IsEntity && HiddenFromViewer(key.EntityKey)) throw KeyGoneForView(new EntityId(key.EntityKey));
        }

        private IReadOnlyList<Value> RowKeyValues(EntityId id, ComponentType component)
        {
            var keys = new List<Value>();
            switch (component.KeyKind)
            {
                case KeyKind.Def:
                    foreach (var key in RowKeys(id, component.Name)) keys.Add(new StrValue(key));
                    break;
                case KeyKind.Entity:
                    foreach (var key in ViewFiltered(RowEntities(id, component.Name), null)) keys.Add(new EntityView(this, key));
                    break;
                default:
                    foreach (var key in RowInts(id, component.Name)) keys.Add(new IntValue(key));
                    break;
            }
            return keys;
        }
    }
}
