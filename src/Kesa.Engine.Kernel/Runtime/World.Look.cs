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
            table.Add(PureBuiltins.MarkName, new PureBuiltin(PureBuiltins.MarkName, MarkBuiltin));
        }

        private Value LookBuiltin(IReadOnlyList<Value> positional, IReadOnlyDictionary<string, Value> keywords)
        {
            ScreenOnly("look()");
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
            ScreenOnly("at()");
            NoPlaceKeywords("at()", keywords);
            const string usage = "write at(\"hex\", (3, 4)), or at((3, 4)) for the game's default space (spec-gaps G394)";
            if (positional.Count == 1) return WhereValue.At(null, Savable("the value of at()", positional[0], usage, hidden: true));
            if (positional.Count != 2) throw LookError("at() takes a space and a value, got " + positional.Count + " arguments", usage);
            return WhereValue.At(Name("at()", "the space name", positional[0]),
                Savable("the value of at()", positional[1], "a place holds " + PlainHint + " (spec-gaps G394)", hidden: true));
        }

        private Value OntoBuiltin(IReadOnlyList<Value> positional, IReadOnlyDictionary<string, Value> keywords)
        {
            ScreenOnly("onto()");
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
            ScreenOnly("between()");
            NoPlaceKeywords("between()", keywords);
            if (positional.Count != 2 || !(positional[0] is WhereValue first) || !(positional[1] is WhereValue second))
                throw LookError("between() takes two places made by at(), onto() or between()",
                    "write between(at(\"hex\", a), onto(ship)) (spec-gaps G394)");
            return WhereValue.Between(first, second);
        }

        private Value MarkBuiltin(IReadOnlyList<Value> positional, IReadOnlyDictionary<string, Value> keywords)
        {
            ScreenOnly("mark()");
            const string usage = "write mark(look(\"spot\"), at=at(\"hex\", [3, 4]), click=[\"mymod.go\", {\"to\": [3, 4]}]) (spec-gaps G395)";
            if (positional.Count != 1) throw LookError("mark() takes a look, got " + positional.Count + " arguments", usage);
            var look = positional[0] switch
            {
                LookValue l => l,
                StrValue s when s.Value.Length > 0 => new LookValue(s.Value, new SortedDictionary<string, Value>(StringComparer.Ordinal)),
                _ => throw LookError("mark() takes look(...) or the name of a look, not " + positional[0].KindName, usage),
            };
            WhereValue? where = null;
            string? action = null;
            SortedDictionary<string, Value>? arguments = null;
            Value? key = null;
            foreach (var pair in keywords)
            {
                switch (pair.Key)
                {
                    case "at":
                        where = pair.Value as WhereValue ??
                                throw LookError("at= of mark() takes at(...), onto(...) or between(...), not " + pair.Value.KindName, usage);
                        break;
                    case "click":
                        if (pair.Value is NoneValue) break;
                        action = Click(pair.Value, out arguments);
                        break;
                    case "key":
                        if (!(pair.Value is NoneValue))
                            key = Savable("key= of mark()", pair.Value, "a key holds " + PlainHint + " (spec-gaps G396)", hidden: true);
                        break;
                    default:
                        throw LookError("mark() has no argument '" + pair.Key + "'", "mark() takes at=, click= and key= (spec-gaps G395, G396)");
                }
            }
            if (where == null) throw LookError("mark() needs at= to say where the mark goes", usage);
            return new MarkValue(look, where, action, arguments, key);
        }

        /// <summary><c>click=[동작, 인자]</c>: 있는 <c>@ui_action</c>과 평범한 값의 사전.</summary>
        private string Click(Value value, out SortedDictionary<string, Value> arguments)
        {
            const string usage = "write click=[\"mymod.go\", {\"to\": [3, 4]}] with a @ui_action and its arguments (spec-gaps G395)";
            if (!(value is ListValue list) || list.Items.Count != 2 || !(list.Items[0] is StrValue name) || !(list.Items[1] is DictValue dict))
                throw LookError("click= of mark() takes [the @ui_action, a dict of its arguments], not " + Describe(value), usage);
            if (!_uiActions.ContainsKey(name.Value))
                throw LookError("there is no @ui_action '" + name.Value + "' for click= of mark()",
                    _commands.ContainsKey(name.Value)
                        ? "'" + name.Value + "' is a command; point click= to a @ui_action that submits it (spec-gaps G395)"
                        : Suggest.Hint(name.Value, _uiActions.Keys));
            arguments = new SortedDictionary<string, Value>(StringComparer.Ordinal);
            foreach (var item in dict.Items)
            {
                if (!(item.Key is StrValue k))
                    throw LookError("the arguments in click= of mark() are named by str, not " + item.Key.KindName, usage);
                arguments[k.Value] = Savable("an argument in click= of mark()", item.Value, "an argument holds " + PlainHint + " (spec-gaps G395)",
                    hidden: true);
            }
            return name.Value;
        }

        private static string Describe(Value value) => value is ListValue list ? "a list of " + list.Items.Count : value.KindName;

        /// <summary>이름으로 보이지 않는 길(칸 값을 변수에 담아 부름)로 게임 코드가 만들면 실행 중에 막는다: 멈춘 흐름에 남으면 저장할 수 없다.</summary>
        private void ScreenOnly(string function)
        {
            if (_viewer == null)
                throw new ScriptRuntimeException(Codes.RunNotWritable, function + " can only be used on a screen", FunctionEffects.LookHint);
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
        internal CallResult WorldObjectAs(WorldView viewer, UiState ui, EntityId id)
        {
            NoHostWrites("WorldObject");
            return ViewingWithUi(viewer, ui, () => Traced("#" + id.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), reads =>
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
        }

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
                // 로드 검사가 함수와 (Look의) 글자만 들인다
                _ => throw new InvalidOperationException("'" + label + "' holds a " + raw.KindName),
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

        // ---- 표시 (G395·G396·G397) ----

        private readonly IReadOnlyDictionary<string, CommandDecl> _worldMarks =
            new SortedDictionary<string, CommandDecl>(StringComparer.Ordinal);

        /// <summary><c>@world_marks</c> 함수의 id (<c>모드id.함수이름</c>, 서수 순).</summary>
        public IEnumerable<string> WorldMarkFunctions => _worldMarks.Keys;

        /// <summary>그 표시 함수를 연결의 UI 기록과 함께 부른다. 값은 <see cref="MarkValue"/>의 리스트이고 <c>None</c>은 빈 리스트로 본다.</summary>
        internal CallResult WorldMarksAs(WorldView viewer, UiState ui, string function)
        {
            NoHostWrites("WorldMarks");
            if (!_worldMarks.TryGetValue(function, out var decl))
                throw new WorldException(Codes.RunUnknownName, "there is no @world_marks '" + function + "'", Suggest.Hint(function, _worldMarks.Keys));
            return ViewingWithUi(viewer, ui, () => Traced(function, _ =>
            {
                var value = RunGateFunction(decl.Function, Array.Empty<Value>(), function);
                if (value is NoneValue) return new ListValue(new List<Value>());
                if (value is ListValue list)
                {
                    foreach (var item in list.Items)
                        if (!(item is MarkValue))
                            throw new WorldException(Codes.RunType, "'" + function + "' returned a list holding " + item.KindName +
                                                                    ", but a @world_marks function gives a list of mark(...) (spec-gaps G395)");
                    return list;
                }
                throw new WorldException(Codes.RunType, "'" + function + "' returned " + value.KindName +
                                                        ", but a @world_marks function gives a list of mark(...) (spec-gaps G395)");
            }));
        }

        // ---- 판 위 입력 (G416·G419·G420) ----

        private readonly IReadOnlyList<WorldInputDecl> _worldInputs = Array.Empty<WorldInputDecl>();

        /// <summary>
        /// 커서 아래 엔티티(위에서부터)에 들어온 입력을 그 타입의 <c>@world_input</c> 화면 동작에 주고, 엔티티가 모두 받지 않으면 커서 아래 자리
        /// (<paramref name="space"/>의 <paramref name="place"/>)를 그 공간의 핸들러에 준다 (G422). 같은 대상은 나중 모드부터 해 보아 사전 조건을
        /// 통과한 첫 것에서 멈춘다. 끝내 못 주면 처음 막힌 결과, 받을 핸들러가 없으면 null.
        /// </summary>
        internal ActResult? WorldInputAs(WorldView viewer, UiState ui, string input, IReadOnlyList<EntityId> entities, string? space, Value? place)
        {
            ActResult? refused = null;
            foreach (var id in entities)
            {
                if (!_entities.TryGetValue(id.Value, out var entity) || !viewer.Sees(id)) continue;
                var taken = TryWorldInput(viewer, ui, input, entity.Def.Type.Name, false, new EntityValue(id), ref refused);
                if (taken != null) return taken;
            }
            if (space != null && place != null)
                return TryWorldInput(viewer, ui, input, space, true, place, ref refused) ?? refused;
            return refused;
        }

        private ActResult? TryWorldInput(WorldView viewer, UiState ui, string input, string target, bool space, Value value, ref ActResult? refused)
        {
            for (int i = _worldInputs.Count - 1; i >= 0; i--)
            {
                var handler = _worldInputs[i];
                if (handler.Input != input || handler.Target != target || handler.Space != space || handler.Parameter == null) continue;
                var args = new SortedDictionary<string, Value>(StringComparer.Ordinal) { [handler.Parameter] = value };
                var result = ActAs(viewer, ui, handler.Action.Id, args);
                // 스크립트 오류는 넘기지 않는다: 모드 버그가 아래 핸들러에 가려지지 않게
                if (result.Outcome == ActOutcome.Succeeded || result.Outcome == ActOutcome.ScriptError) return result;
                if (result.Outcome == ActOutcome.PreconditionsFailed && refused == null) refused = result;
            }
            return null;
        }
    }
}
