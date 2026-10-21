using System.Collections.Generic;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Host;
using Kesa.Engine.Loading;
using Kesa.Engine.Schema;
using Kesa.Engine.Script;

namespace Kesa.Engine.Runtime
{
    /// <summary>
    /// Def 타입 바로 아래의 <c>const</c> 읽기: 엔티티가 자기 Def의 것을 타입 이름으로(G107), Def id에서 <c>defdata</c>로(G303·G304).
    /// </summary>
    public sealed partial class World
    {
        // ---- 엔티티의 Def 상수 (G107) ----

        /// <summary>엔티티의 Def 타입 이름으로 그 타입 바로 아래의 <c>const</c>들을 본다 (<c>g.ConflictGoal.succeeded</c>, G107). 다른 타입이면 None.</summary>
        private Value DefOrNone(EntityRecord entity, DefType type) =>
            entity.Def.Type == type ? new DefView(this, entity.Id, type) : (Value)NoneValue.Instance;

        private Value DefConst(long id, DefType type, string name)
        {
            var entity = Record(new EntityId(id), AliveRead);
            var value = ConstOf(entity.Def, type, name);
            _trace?.Add(new ExplainNode(ExplainKind.Read, new EntityId(id), type.Name, null, name, value, new List<ExplainNode>()));
            return value;
        }

        /// <summary>Def 하나의 <c>const</c> 값. 없는 이름은 "혹시 …?"가 붙은 오류다. 이름으로 적은 호스트 함수는 이 월드의 것으로 바꾼다.</summary>
        private Value ConstOf(FrozenDef def, DefType type, string name)
        {
            MemberDecl? found = null;
            foreach (var decl in type.Consts)
                if (decl.Name == name) found = decl;
            if (found == null)
            {
                var names = new List<string>();
                foreach (var decl in type.Consts) names.Add(decl.Name);
                throw new ScriptRuntimeException(Codes.RunScript, "Def type '" + type.Name + "' has no const '" + name + "'",
                    Suggest.Hint(name, names));
            }
            def.Consts.TryGet(name, out var raw);
            return BindHostReferences(Normalize(found.Spec.Type, raw.Value));
        }

        /// <summary>
        /// Def 값 자리에 이름으로 적은 호스트 함수(목록 안 포함)를 이 월드의 호스트 함수 값으로 바꾼다 (G291). 없으면 값을 그대로 돌려준다.
        /// </summary>
        private Value BindHostReferences(Value value)
        {
            switch (value)
            {
                case HostReference reference:
                    return HostValue(reference.Name) ?? throw new ScriptRuntimeException(Codes.RunScript,
                        "the host function " + reference.Name + " is not registered in this game");
                case ListValue list:
                    List<Value>? bound = null;
                    for (int i = 0; i < list.Items.Count; i++)
                    {
                        var item = BindHostReferences(list.Items[i]);
                        if (bound == null && ReferenceEquals(item, list.Items[i])) continue;
                        if (bound == null)
                        {
                            bound = new List<Value>(list.Items.Count);
                            for (int j = 0; j < i; j++) bound.Add(list.Items[j]);
                        }
                        bound.Add(item);
                    }
                    return bound == null ? value : new ListValue(bound);
            }
            return value;
        }

        /// <summary>엔티티의 Def 타입 바로 아래 <c>const</c>들 (Def마다 하나의 값, 엔티티가 공유한다).</summary>
        private sealed class DefView : Value, IRuntimeAttributes, IValueEquality
        {
            /// <summary>같은 엔티티의 같은 Def 타입이면 같다 (G142).</summary>
            public bool ValueEquals(Value other) => other is DefView v && v._id == _id && v._type == _type;

            private readonly World _world;
            private readonly long _id;
            private readonly DefType _type;

            public DefView(World world, long id, DefType type)
            {
                _world = world;
                _id = id;
                _type = type;
            }

            public override string KindName => _type.Name;

            public long Id => _id;
            public DefType Type => _type;

            public Value GetAttribute(string name) => _world.DefConst(_id, _type, name);
        }

        // ---- defs (G305) ----

        /// <summary>
        /// <c>defs(City)</c>: 그 타입의 Def들의 전체 id. 순서는 등록 순서(모드 로드 순서 뒤 파일 순서), <c>abstract</c>는 동결본에 없어 빠진다(G70).
        /// 부른 모드가 의존하지 않는 모드의 Def도 나온다.
        /// </summary>
        private Value DefsBuiltin(IReadOnlyList<Value> positional, IReadOnlyDictionary<string, Value> keywords)
        {
            if (positional.Count != 1 || keywords.Count > 0)
                throw new ScriptRuntimeException(Codes.RunType,
                    "defs() takes one Def type, got " + (positional.Count + keywords.Count) + " arguments", "write defs(City)");
            // 맨 이름이 뜻하는 타입 값만 받는다 (G306·G307)
            string name = SchemaNameArgument(PureBuiltins.DefsName, positional[0]);
            if (!_schema.TryGetType(name, out _))
                throw new ScriptRuntimeException(Codes.RunType, "'" + name + "' is not a Def type", "write defs(City) with a Def type");
            var ids = new List<Value>();
            foreach (var def in _defs.OfType(name)) ids.Add(new StrValue(def.Id));
            return new ListValue(ids);
        }

        // ---- defdata (G303·G304) ----

        /// <summary>
        /// <c>defdata("Medic")</c>: 그 Def를 보는 값. 짧은 이름은 <c>defid</c>처럼 부른 함수의 모드 기준이고(G261), 없는 Def는 오류다.
        /// </summary>
        private Value DefDataBuiltin(string mod, IReadOnlyList<Value> positional, IReadOnlyDictionary<string, Value> keywords)
        {
            if (positional.Count != 1 || keywords.Count > 0 || !(positional[0] is StrValue))
                throw new ScriptRuntimeException(Codes.RunType,
                    "defdata() takes one Def id string, got " +
                    (positional.Count == 1 && keywords.Count == 0 ? positional[0].KindName : positional.Count + keywords.Count + " arguments"),
                    "write defdata(\"Medic\") or defdata(k) with a Def id value");
            string id = ((StrValue)DefIdBuiltin(mod, positional, keywords)).Value;
            _defs.TryGet(id, out var def);
            return new DefDataView(this, def, null);
        }

        /// <summary>
        /// <c>defdata(id)</c>의 값. <see cref="_type"/>이 없으면 Def 자체이고, Def 타입 이름 속성으로 그 타입의 <c>const</c>들(그 타입이 아니면
        /// None, G107과 같음)로 간다. 있으면 그 <c>const</c>들이다. Def는 바뀌지 않으므로 <c>@when</c> 읽기 기록이 없다.
        /// </summary>
        private sealed class DefDataView : Value, IRuntimeAttributes, IValueEquality
        {
            private readonly World _world;
            private readonly FrozenDef _def;
            private readonly DefType? _type;

            public DefDataView(World world, FrozenDef def, DefType? type)
            {
                _world = world;
                _def = def;
                _type = type;
            }

            public FrozenDef Def => _def;
            public DefType? Type => _type;

            public override string KindName => _type == null ? "Def data" : _type.Name;

            /// <summary>같은 Def의 같은 단계면 같다 (G142).</summary>
            public bool ValueEquals(Value other) => other is DefDataView v && v._def.Id == _def.Id && v._type == _type;

            public Value GetAttribute(string name)
            {
                if (_type != null)
                {
                    var value = _world.ConstOf(_def, _type, name);
                    _world._trace?.Add(new ExplainNode(ExplainKind.Read, default, _type.Name, _def.Id, name, value, new List<ExplainNode>()));
                    return value;
                }
                if (_world._schema.TryGetType(name, out var type))
                    return _def.Type == type ? new DefDataView(_world, _def, type) : (Value)NoneValue.Instance;
                var names = new List<string>();
                foreach (var t in _world._schema.Types) names.Add(t.Name);
                throw new ScriptRuntimeException(Codes.RunScript,
                    "defdata(\"" + _def.Id + "\") has no Def type '" + name + "'; read its consts through its Def type, like ." +
                    _def.Type.Name + ".name", Suggest.Hint(name, names));
            }
        }
    }
}
