using System;
using System.Collections.Generic;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Schema;
using Kesa.Engine.Script;

namespace Kesa.Engine.Runtime
{
    /// <summary>월드 표시의 값 (spec-gaps G388·G389·G394·G406): 그림 <c>look</c>과 자리 <c>at</c>·<c>onto</c>·<c>between</c>.</summary>
    public sealed partial class World
    {
        private const string PlainHint = "numbers, str, text(...), entities and lists or dicts of them";

        private void AddLookBuiltins(SortedDictionary<string, Value> table)
        {
            table.Add(PureBuiltins.LookName, new PureBuiltin(PureBuiltins.LookName, LookBuiltin));
            table.Add(PureBuiltins.AtName, new PureBuiltin(PureBuiltins.AtName, AtBuiltin));
            table.Add(PureBuiltins.OntoName, new PureBuiltin(PureBuiltins.OntoName, OntoBuiltin));
            table.Add(PureBuiltins.BetweenName, new PureBuiltin(PureBuiltins.BetweenName, BetweenBuiltin));
        }

        private Value LookBuiltin(IReadOnlyList<Value> positional, IReadOnlyDictionary<string, Value> keywords)
        {
            if (positional.Count != 1)
                throw LookError("look() takes the name of a look, got " + positional.Count + " arguments",
                    "write look(\"infantry\", hurt=True) (spec-gaps G389)");
            string name = Name("look()", "the name of a look", positional[0]);
            var keys = new SortedDictionary<string, Value>(StringComparer.Ordinal);
            foreach (var pair in keywords)
                keys[pair.Key] = Savable(pair.Key + "= of look()", pair.Value, "a look holds " + PlainHint + " (spec-gaps G389)", hidden: true);
            return new LookValue(name, keys);
        }

        private Value AtBuiltin(IReadOnlyList<Value> positional, IReadOnlyDictionary<string, Value> keywords)
        {
            NoPlaceKeywords("at()", keywords);
            const string usage = "write at(\"hex\", (3, 4)), or at((3, 4)) for the game's default space (spec-gaps G394)";
            if (positional.Count == 1) return WhereValue.At(null, Savable("the value of at()", positional[0], usage, hidden: true));
            if (positional.Count != 2) throw LookError("at() takes a space and a value, got " + positional.Count + " arguments", usage);
            return WhereValue.At(Name("at()", "the space name", positional[0]),
                Savable("the value of at()", positional[1], "a place holds " + PlainHint + " (spec-gaps G394)", hidden: true));
        }

        private Value OntoBuiltin(IReadOnlyList<Value> positional, IReadOnlyDictionary<string, Value> keywords)
        {
            NoPlaceKeywords("onto()", keywords);
            const string usage = "write onto(ship, \"deck\") (spec-gaps G406)";
            if (positional.Count < 1 || positional.Count > 2)
                throw LookError("onto() takes an entity and where on it, got " + positional.Count + " arguments", usage);
            Value target = positional[0] switch
            {
                IEntityReference entity => new EntityValue(entity.EntityId),
                HiddenValue hidden => hidden,
                _ => throw LookError("onto() takes an entity, not " + positional[0].KindName, usage),
            };
            string? point = positional.Count == 2 ? Name("onto()", "the attach point", positional[1]) : null;
            return WhereValue.Onto(target, point);
        }

        private Value BetweenBuiltin(IReadOnlyList<Value> positional, IReadOnlyDictionary<string, Value> keywords)
        {
            NoPlaceKeywords("between()", keywords);
            if (positional.Count != 2 || !(positional[0] is WhereValue first) || !(positional[1] is WhereValue second))
                throw LookError("between() takes two places made by at(), onto() or between()",
                    "write between(at(\"hex\", a), onto(ship)) (spec-gaps G394)");
            return WhereValue.Between(first, second);
        }

        private static string Name(string function, string what, Value value)
        {
            if (value is StrValue s && s.Value.Length > 0) return s.Value;
            throw LookError(function + " takes " + what + " as a non-empty str, not " + (value is StrValue ? "an empty str" : value.KindName), null);
        }

        private static void NoPlaceKeywords(string function, IReadOnlyDictionary<string, Value> keywords)
        {
            foreach (var pair in keywords) throw LookError(function + " has no argument '" + pair.Key + "'", null);
        }

        private static ScriptRuntimeException LookError(string message, string? hint) =>
            new ScriptRuntimeException(Codes.RunType, message, hint);

        // ---- 월드 물체 (G401·G403·G405·G407) ----

        internal const string LookSlot = "look";
        internal const string WhereSlot = "where";

        /// <summary>이 타입의 엔티티를 지도에 올리나: 이름이 <c>look</c>인 <c>Look</c> 칸과 <c>where</c>인 <c>Where</c> 칸이 둘 다 있다 (G403).</summary>
        private static bool IsWorldType(DefType type)
        {
            bool look = false, where = false;
            foreach (var decl in type.Consts)
            {
                if (!(decl.Spec.Type is FunctionTypeRef slot)) continue;
                if (decl.Name == LookSlot && slot.Kind == FunctionSlotKind.Look) look = true;
                if (decl.Name == WhereSlot && slot.Kind == FunctionSlotKind.Where) where = true;
            }
            return look && where;
        }

        /// <summary>그 연결이 보는, 지도에 올릴 수 있는 타입의 엔티티 (id 순). 그릴지는 <see cref="WorldObjectAs"/>가 정한다.</summary>
        internal IReadOnlyList<EntityId> WorldEntitiesAs(WorldView viewer)
        {
            var ids = new SortedSet<long>();
            foreach (var type in _schema.Types)
                if (IsWorldType(type) && _byType.TryGetValue(type, out var set)) ids.UnionWith(set);
            var result = new List<EntityId>(ids.Count);
            foreach (var id in ids)
                if (viewer.Sees(new EntityId(id))) result.Add(new EntityId(id));
            return result;
        }

        /// <summary>
        /// 그 엔티티의 <c>[그림, 자리]</c>를 연결의 UI 기록과 함께 계산한다. 없거나 못 보거나 올릴 타입이 아니거나 칸 하나가 <c>None</c>이면
        /// <c>None</c>이다 (G405). 영수증은 엔티티가 죽거나 보기 정책이 바뀌는 것도 본다.
        /// </summary>
        internal CallResult WorldObjectAs(WorldView viewer, UiState ui, EntityId id) =>
            ViewingWithUi(viewer, ui, () => Traced("#" + id.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), reads =>
            {
                reads.Add(new CallRead(id.Value, ""));
                if (!_entities.TryGetValue(id.Value, out var entity) || !viewer.Sees(id) || !IsWorldType(entity.Def.Type))
                    return NoneValue.Instance;
                var look = SlotValue(entity, id, LookSlot);
                if (look is NoneValue) return look;
                var where = SlotValue(entity, id, WhereSlot);
                if (where is NoneValue) return where;
                return new ListValue(new[] { look, where });
            }));

        private Value SlotValue(EntityRecord entity, EntityId id, string slot)
        {
            string label = entity.Def.Id + "." + slot;
            Value raw;
            try
            {
                raw = ConstOf(entity.Def, entity.Def.Type, slot);
            }
            catch (ScriptRuntimeException e)
            {
                throw ToWorldException(e, label);
            }
            Value value = raw switch
            {
                StrValue name when slot == LookSlot => name,
                FunctionValue fn => RunGateFunction(fn, new Value[] { new EntityView(this, id) }, label),
                _ => throw new WorldException(Codes.RunType, "'" + label + "' holds a " + raw.KindName + ", which cannot be called here yet"),
            };
            switch (value)
            {
                case NoneValue _:
                case LookValue _ when slot == LookSlot:
                case WhereValue _ when slot == WhereSlot:
                    return value;
                case StrValue name when slot == LookSlot && name.Value.Length > 0:
                    return new LookValue(name.Value, new SortedDictionary<string, Value>(StringComparer.Ordinal));
            }
            throw new WorldException(Codes.RunType, "'" + label + "' returned " + value.KindName + ", but a " + slot + " slot gives " +
                                                    (slot == LookSlot ? "look(...) or the name of a look" : "at(...), onto(...) or between(...)") +
                                                    ", or None to leave it off the screen (spec-gaps G405)");
        }
    }
}
