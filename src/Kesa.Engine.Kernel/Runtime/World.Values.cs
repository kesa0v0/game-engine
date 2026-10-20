using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Loading;
using Kesa.Engine.Numerics;
using Kesa.Engine.Schema;
using Kesa.Engine.Script;

namespace Kesa.Engine.Runtime
{
    /// <summary>칸 값의 계획과 검사(생성 순간의 값 정하기, 쓰기 검사, 읽기 순서)와 역방향 색인 (G89·G91~G93).</summary>
    public sealed partial class World
    {
        // ---- 값 계획과 검사 ----

        /// <summary>
        /// 생성 순간(G89·G93)에 한 컴포넌트(또는 줄)의 값을 정한다. 엔티티에 저장할 값만 <paramref name="target"/>에 넣는다:
        /// 준 처음 값과 <c>default_from</c>으로 계산한 값. 어느 길로도 값이 정해지지 않는 칸이 있으면 실패한다.
        /// </summary>
        private void PlanMembers(Planned target, ComponentType component,
            OrderedMap<ResolvedValue>? defMembers, Dictionary<MemberDecl, Value> given, string where, RowKey? row = null)
        {
            foreach (var member in component.Members)
            {
                var spec = member.Spec;
                switch (member.Kind)
                {
                    case MemberKind.Derived:
                        continue;
                    case MemberKind.Const:
                        if (defMembers == null || !defMembers.Contains(member.Name)) RequireConstDefault(component, member, where);
                        continue;
                }

                if (given.TryGetValue(member, out var value))
                {
                    target.Add(member, value);
                    continue;
                }
                if (defMembers != null && defMembers.Contains(member.Name)) continue;
                if (spec.Default is FunctionValue fn)
                {
                    // 엔티티가 만들어진 뒤에 계산한다 (FinishPending)
                    target.Pending.Add(new PendingDefault(component, row, member, fn, null, where));
                    continue;
                }
                if (spec.Default != null) continue;
                if (spec.DefaultFrom is NameRef from)
                {
                    component.TryGetMember(from.Name, out var sourceMember);
                    if (sourceMember.Kind == MemberKind.Derived)
                    {
                        target.Pending.Add(new PendingDefault(component, row, member, null, sourceMember, where));
                        continue;
                    }
                    var source = SourceValue(component, from.Name, defMembers, given, where);
                    target.Add(member, CheckWrite(member.Display, member, source));
                    continue;
                }
                if (member.Kind == MemberKind.Ref && spec.Optional) continue;
                throw new WorldException(Codes.RunMissingValue,
                    "'" + component.Name + "." + member.Name + "' has no value (" + where + ")",
                    member.Kind == MemberKind.Ref
                        ? "a required ref must be given when the entity is created (spec-gaps G91)"
                        : "give it as an initial value, or declare a default (spec-gaps G93)");
            }
        }

        /// <summary><c>default_from</c>이 가리키는 같은 컴포넌트의 값 (생성 순간 기준).</summary>
        private Value SourceValue(ComponentType component, string name, OrderedMap<ResolvedValue>? defMembers,
            Dictionary<MemberDecl, Value> given, string where)
        {
            component.TryGetMember(name, out var source);
            if (given.TryGetValue(source, out var v)) return v;
            if (defMembers != null && defMembers.TryGet(name, out var d)) return d.Value;
            if (source.Spec.Default != null && !(source.Spec.Default is FunctionValue)) return source.Spec.Default;
            throw new WorldException(Codes.RunMissingValue,
                "'" + component.Name + "." + name + "' has no value to start '" + component.Name + "' from (" + where + ")");
        }

        private static void RequireConstDefault(ComponentType component, MemberDecl member, string where)
        {
            if (member.Kind != MemberKind.Const || member.Spec.Default != null) return;
            if (member.Spec.Type is ListTypeRef || member.Spec.Type is DictTypeRef) return; // 빈 컬렉션 (G67)
            throw new WorldException(Codes.RunMissingValue,
                "'" + component.Name + "." + member.Name + "' is a const without a default, so it has no value outside a Def (" + where + ")",
                "declare a default for it (spec-gaps G93)");
        }

        /// <summary>처음 값들을 검사해 멤버별로 모은다. 다른 컴포넌트의 값이나 같은 칸을 두 번 주면 오류.</summary>
        private Dictionary<MemberDecl, Value> Given(Init[] inits, Func<MemberHandle, bool> belongs, string notBelonging)
        {
            var given = new Dictionary<MemberDecl, Value>();
            foreach (var init in inits)
            {
                if (!belongs(init.Member))
                    throw new WorldException(Codes.RunNoComponent, "cannot give '" + init.Member.Display + "' here: " + notBelonging);
                if (given.ContainsKey(init.Member.Member))
                    throw new WorldException(Codes.RunType, "'" + init.Member.Display + "' is given twice");
                given.Add(init.Member.Member, CheckWrite(init.Member.Display, init.Member.Member, init.Value));
            }
            return given;
        }

        /// <summary>쓸 수 있는 칸인지, 값의 타입과 범위가 맞는지 (G92). 저장할 값(정규화된 값)을 돌려준다.</summary>
        private Value CheckWrite(string display, MemberDecl member, Value value)
        {
            var spec = member.Spec;
            switch (member.Kind)
            {
                case MemberKind.Const:
                    throw new WorldException(Codes.RunNotWritable, "'" + display + "' is a const value and cannot be written");
                case MemberKind.Derived:
                    throw new WorldException(Codes.RunNotWritable, "'" + display + "' is a derived value and cannot be written");
                case MemberKind.Ref:
                    string targetType = ((EntityRefTypeRef)spec.Type).Target.Name;
                    if (value is NoneValue)
                    {
                        if (spec.Optional) return value;
                        throw new WorldException(Codes.RunType, "'" + display + "' is a required ref and cannot be None");
                    }
                    if (!(value is EntityValue ev))
                        throw new WorldException(Codes.RunType, "'" + display + "' takes an entity, not " + value.KindName);
                    if (!_entities.TryGetValue(ev.Id.Value, out var target))
                        throw new WorldException(Codes.RunDeadRef, "'" + display + "' cannot refer to " + ev.Id + ", which does not exist");
                    if (target.Def.Type.Name != targetType)
                        throw new WorldException(Codes.RunType,
                            "'" + display + "' refers to " + targetType + ", but " + ev.Id + " is a " + target.Def.Type.Name);
                    return value;
            }

            string? problem = PatchOperationCheck.Mismatch(spec.Type, value);
            if (problem != null) throw new WorldException(Codes.RunType, "'" + display + "': " + problem);
            if (spec.Type is DefIdTypeRef defIdType)
            {
                var str = (StrValue)value;
                string id = ScriptDefId(str.Value);
                if (!_defs.TryGet(id, out var def) || def.Type.Name != defIdType.Target.Name)
                {
                    var ids = new List<string>();
                    foreach (var d in _defs.OfType(defIdType.Target.Name)) ids.Add(d.Id);
                    throw UnknownDef("'" + display + "': there is no " + defIdType.Target.Name + " '" + id + "'", id, ids);
                }
                if (id != str.Value) value = new StrValue(id, str.Origin);
            }
            var normalized = Normalize(spec.Type, value);
            if (spec.Min != null && CompareValues(normalized, spec.Min) < 0)
                throw new WorldException(Codes.RunRange, "'" + display + "': " + Show(normalized) + " is below min " + Show(spec.Min),
                    "clamp the value explicitly (spec-gaps G92)");
            if (spec.Max != null && CompareValues(normalized, spec.Max) > 0)
                throw new WorldException(Codes.RunRange, "'" + display + "': " + Show(normalized) + " is above max " + Show(spec.Max),
                    "clamp the value explicitly (spec-gaps G92)");
            return normalized;
        }

        /// <summary>엔티티 값 → Def 값 → 스키마 기본값 (Flyweight, spec S4).</summary>
        private Value Read(RowKey? row, ComponentType component, MemberDecl member, OrderedMap<ResolvedValue>? defMembers,
            Dictionary<MemberDecl, Value> values)
        {
            var spec = member.Spec;
            if (member.Kind == MemberKind.Derived)
                throw new WorldException(Codes.RunNotComputed, "'" + Shown(member, row) + "' is derived and cannot be computed yet (spec-gaps G90)");

            Value? value = null;
            if (member.Kind != MemberKind.Const && values.TryGetValue(member, out var own)) value = own;
            else if (defMembers != null && defMembers.TryGet(member.Name, out var fromDef)) value = fromDef.Value;
            else if (spec.Default != null && !(spec.Default is FunctionValue)) value = spec.Default;
            else if (member.Kind == MemberKind.Const && (spec.Type is ListTypeRef || spec.Type is DictTypeRef))
                value = spec.Type is ListTypeRef ? (Value)new ListValue(new List<Value>()) : new DictValue(new List<DictItem>());
            else if (member.Kind == MemberKind.Ref && spec.Optional) value = NoneValue.Instance;

            // 생성 순간의 검사(G93)가 구멍을 막으므로 여기 오면 월드의 버그다
            // (함수 기본값이 아직 계산되지 않은 칸을 읽은 경우도 여기다: 선언 순서상 뒤의 칸이다, G106)
            if (value == null)
                throw new WorldException(Codes.RunMissingValue, "'" + Shown(member, row) + "' has no value yet",
                    "a default computed at creation can only read members declared before it (spec-gaps G106)");

            // Ref 칸은 대상이 살아 있는지에 따라 읽는 값이 달라진다(G91): when 조건이면 대상도 읽은 것이다(영수증은 살았는지만, G290)
            if (member.Kind == MemberKind.Ref && value is EntityValue target) Read(target.Id.Value, AliveRead);
            if (member.Kind == MemberKind.Ref && value is EntityValue ev && !_entities.ContainsKey(ev.Id.Value))
            {
                if (spec.Optional) return NoneValue.Instance;
                throw new WorldException(Codes.RunDeadRef, "'" + Shown(member, row) + "' refers to " + ev.Id + ", which was destroyed",
                    "declare the ref optional=True if it may outlive its target (spec-gaps G91)");
            }
            return member.Kind == MemberKind.Ref ? value : Normalize(spec.Type, value);
        }

        /// <summary>진단의 칸 이름 <c>Skill.level['Cooking']</c>. 오류일 때만 만든다(읽을 때마다 이어 붙이지 않는다).</summary>
        private static string Shown(MemberDecl member, RowKey? row) =>
            row == null ? member.Display : member.Display + "[" + row.Value + "]";

        /// <summary><c>fixed</c> 칸의 정수는 <c>fixed</c>로 바꾼다 (Def 리터럴은 정수로 적을 수 있다).</summary>
        private static Value Normalize(TypeRef type, Value value)
        {
            if (!ReferenceEquals(type, PrimitiveTypeRef.Fixed) || !(value is IntValue i)) return value;
            try
            {
                return new FixedValue(Fixed.FromRaw(checked(i.Value * Fixed.Scale)));
            }
            catch (OverflowException)
            {
                throw new WorldException(Codes.RunType, Show(value) + " is too large for a fixed value");
            }
        }

        private static BigInteger Scaled(Value value) =>
            value is IntValue i ? new BigInteger(i.Value) * Fixed.Scale : new BigInteger(((FixedValue)value).Value.Raw);

        /// <summary>숫자는 값으로, 문자열은 서수로, bool은 False 먼저 (order_by와 범위 검사).</summary>
        private static int CompareValues(Value a, Value b)
        {
            if ((a is IntValue || a is FixedValue) && (b is IntValue || b is FixedValue)) return Scaled(a).CompareTo(Scaled(b));
            if (a is StrValue sa && b is StrValue sb) return string.CompareOrdinal(sa.Value, sb.Value);
            if (a is BoolValue ba && b is BoolValue bb) return ba.Value.CompareTo(bb.Value);
            return 0;
        }

        internal static string Show(Value value) =>
            value is IntValue i ? i.Value.ToString(CultureInfo.InvariantCulture)
            : value is FixedValue f ? f.Value.ToString()
            : value.KindName;

        // ---- 역방향 색인 ----

        private void IndexRefs(EntityRecord entity, ComponentType component)
        {
            if (component.IsKeyed) return;
            foreach (var member in component.Members)
            {
                if (member.Kind != MemberKind.Ref) continue;
                if (entity.Values.TryGetValue(member, out var v) && v is EntityValue ev) AddInverse(member, ev.Id.Value, entity.Id);
            }
        }

        private void UnindexRefs(EntityRecord entity, ComponentType component)
        {
            if (component.IsKeyed) return;
            foreach (var member in component.Members)
            {
                if (member.Kind != MemberKind.Ref) continue;
                if (entity.Values.TryGetValue(member, out var v) && v is EntityValue ev) RemoveInverse(member, ev.Id.Value, entity.Id);
            }
        }

        private void Reindex(MemberDecl member, long source, Dictionary<MemberDecl, Value> values, Value next)
        {
            if (values.TryGetValue(member, out var old) && old is EntityValue o) RemoveInverse(member, o.Id.Value, source);
            if (next is EntityValue n) AddInverse(member, n.Id.Value, source);
        }

        private void AddInverse(MemberDecl member, long target, long source)
        {
            if (!_inverse.TryGetValue(member, out var byTarget))
                _inverse.Add(member, byTarget = new Dictionary<long, SortedSet<long>>());
            if (!byTarget.TryGetValue(target, out var sources)) byTarget.Add(target, sources = new SortedSet<long>());
            Touch(target); // 대상의 역방향 컬렉션이 바뀐다
            Stamp(target, member.Spec.Inverse?.Name);
            if (sources.Add(source)) Journal(() => sources.Remove(source));
        }

        private void RemoveInverse(MemberDecl member, long target, long source)
        {
            Touch(target);
            Stamp(target, member.Spec.Inverse?.Name);
            if (_inverse.TryGetValue(member, out var byTarget) && byTarget.TryGetValue(target, out var sources) &&
                sources.Remove(source))
                Journal(() => sources.Add(source));
        }
    }
}
