using System;
using System.Collections.Generic;
using System.Globalization;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Script;

namespace Kesa.Engine.Runtime
{
    /// <summary>멈추는 내장: <c>ask</c>(G138)·<c>wait</c>·스크립트 <c>advance</c>(G167), 대상 <c>about=</c>(G165), 마감과 기본 답(G160~G162), 답 값 <c>r</c>.</summary>
    public sealed partial class World
    {
        // ---- ask (G138) ----

        private void AddFlowBuiltins(SortedDictionary<string, Value> table)
        {
            table.Add(PureBuiltins.AskName,
                new PureBuiltin(PureBuiltins.AskName, (p, k) => Guard(() => Ask("", p, k)))
                {
                    Writes = true,
                    Suspends = true,
                    CallerBody = (mod, p, k) => Guard(() => Ask(mod, p, k)),
                });
            table.Add(PureBuiltins.WaitName,
                new PureBuiltin(PureBuiltins.WaitName, (p, k) => Wait(p, k)) { Writes = true, Suspends = true });
            table.Add(PureBuiltins.AdvanceName,
                new PureBuiltin(PureBuiltins.AdvanceName, (p, k) => ScriptAdvance(p, k)) { Writes = true, Suspends = true });
        }

        /// <summary><c>wait(ticks=n)</c> (spec X2·S5): n은 1 이상. 멈추는 것은 실행기가 하고, 깨어나면 None을 돌려준다.</summary>
        private Value Wait(IReadOnlyList<Value> positional, IReadOnlyDictionary<string, Value> keywords)
        {
            const string usage = "wait(ticks=3)";
            if (positional.Count > 0 || !keywords.TryGetValue("ticks", out var ticks))
                throw new ScriptRuntimeException(Codes.RunScript, "wait() takes ticks= and an optional about=, like " + usage, "write " + usage);
            foreach (var pair in keywords)
                if (pair.Key != "ticks" && pair.Key != "about")
                    throw new ScriptRuntimeException(Codes.RunScript, "wait() has no argument '" + pair.Key + "'",
                        Suggest.Hint(pair.Key, new[] { "ticks", "about" }));
            if (!(ticks is IntValue n))
                throw new ScriptRuntimeException(Codes.RunType, "ticks= of wait() must be an int, not " + ticks.KindName, "write " + usage);
            if (n.Value < 1)
                throw new ScriptRuntimeException(Codes.RunScript,
                    "ticks= of wait() must be at least 1, got " + n.Value.ToString(CultureInfo.InvariantCulture), "spec X2");
            return new WaitPayload(n.Value) { About = keywords.TryGetValue("about", out var about) ? About("wait", about) : null };
        }

        /// <summary>
        /// <c>about=</c>: 이번 멈춤 동안의 대상 (S5, spec-gaps G165). 엔티티 하나나 엔티티 목록이고, 이미 사라진 엔티티는 오류다.
        /// </summary>
        private List<EntityId> About(string fn, Value value)
        {
            var subjects = new List<EntityId>();
            void One(Value v)
            {
                if (!(v is IEntityReference e))
                    throw new ScriptRuntimeException(Codes.RunType, "about= of " + fn + "() takes entities, not " + v.KindName,
                        "write about=[ruler] for what this pause depends on (spec-gaps G165)");
                if (!IsAlive(e.EntityId))
                    throw new ScriptRuntimeException(Codes.RunScript,
                        "about= of " + fn + "() names " + e.EntityId + ", which is already destroyed");
                if (!subjects.Contains(e.EntityId)) subjects.Add(e.EntityId);
            }
            if (value is ListValue list)
                foreach (var item in list.Items) One(item);
            else One(value);
            return subjects;
        }

        /// <summary>
        /// <c>advance(n)</c> (spec X2, spec-gaps G167): Flow를 멈추고, 이 호출의 신호 처리가 끝난 뒤 엔진이 n틱을 진행하고 이어 간다.
        /// 시간이 흐르는 중이거나 신호 핸들러 안에서는 부를 수 없다.
        /// </summary>
        private Value ScriptAdvance(IReadOnlyList<Value> positional, IReadOnlyDictionary<string, Value> keywords)
        {
            const string usage = "advance(1)";
            if (positional.Count != 1 || keywords.Count != 0)
                throw new ScriptRuntimeException(Codes.RunScript, "advance() takes the number of ticks, like " + usage, "write " + usage);
            if (!(positional[0] is IntValue n) || n.Value < 1)
                throw new ScriptRuntimeException(Codes.RunType,
                    "advance() needs a number of ticks of at least 1, not " + Show(positional[0]), "write " + usage);
            if (_advancing)
                throw new ScriptRuntimeException(Codes.RunScript, "advance() cannot be called while time is already advancing",
                    "a woken flow or a tick handler runs inside an advance; call advance() from a command or after an answer (spec-gaps G167)");
            if (_reacting != null)
                throw new ScriptRuntimeException(Codes.RunScript, "advance() cannot be called from a signal handler",
                    "call advance() from a command or after an answer (spec-gaps G167)");
            if (_settling)
                throw new ScriptRuntimeException(Codes.RunScript, "advance() cannot be called from a @when reaction",
                    "call advance() from a command or after an answer (spec-gaps G167, G169)");
            return new AdvancePayload(n.Value);
        }

        /// <summary><c>advance</c>가 실행기에 넘기는 것: 진행할 틱 수.</summary>
        private sealed class AdvancePayload : Value
        {
            public AdvancePayload(long ticks) => Ticks = ticks;
            public long Ticks { get; }
            public override string KindName => "advance";
        }

        /// <summary><c>wait</c>가 실행기에 넘기는 것: 잘 틱 수와 이번 멈춤의 대상.</summary>
        private sealed class WaitPayload : Value
        {
            public WaitPayload(long ticks) => Ticks = ticks;
            public long Ticks { get; }

            /// <summary><c>about=</c>으로 적은 대상. 적지 않았으면 null (입구 인자를 본다, G165).</summary>
            public List<EntityId>? About { get; set; }

            public override string KindName => "wait";
        }

        /// <summary>
        /// <c>ask(who, title, choices=[...], responses=[...], hint=, data=)</c>의 인자를 검사해 질문의 내용을 만든다 (G138).
        /// 멈추는 것은 실행기가 한다. 응답 커맨드 이름은 부른 함수의 모드에서 찾고, 다른 모드의 것은 <c>"모드id.이름"</c>으로 적는다.
        /// </summary>
        private Value Ask(string mod, IReadOnlyList<Value> positional, IReadOnlyDictionary<string, Value> keywords)
        {
            const string usage = "ask(who, \"Title_Key\", choices=[...], responses=[...])";
            if (positional.Count != 2)
            {
                throw new ScriptRuntimeException(Codes.RunScript,
                    "ask() takes who and the title as positional arguments, got " + positional.Count, "write " + usage);
            }

            var who = new List<EntityId>();
            void Answerer(Value v)
            {
                if (!(v is IEntityReference e))
                    throw new ScriptRuntimeException(Codes.RunType,
                        "ask() asks entities, not " + v.KindName, "pass an entity or a list of entities (spec-gaps G125)");
                if (!IsAlive(e.EntityId))
                    throw new ScriptRuntimeException(Codes.RunScript, "ask() cannot ask the destroyed entity " + e.EntityId);
                if (!who.Contains(e.EntityId)) who.Add(e.EntityId);
            }
            if (positional[0] is ListValue list)
            {
                foreach (var item in list.Items) Answerer(item);
                if (who.Count == 0) throw new ScriptRuntimeException(Codes.RunScript, "ask() needs at least one entity to ask");
            }
            else Answerer(positional[0]);

            Value title = positional[1] switch
            {
                StrValue s => s,
                TextValue t => Savable("the title of ask()", t),
                _ => throw new ScriptRuntimeException(Codes.RunType,
                    "the title of ask() must be a text key (str) or text(...), not " + positional[1].KindName, "write " + usage),
            };

            var choices = new List<OptionValue>();
            var responses = new List<string>();
            var responseNames = new List<string>();
            string? hint = null;
            Value data = NoneValue.Instance;
            bool hasChoices = false;
            bool? blocksTime = null; // 적지 않으면 마감 유무로 정한다 (G159)
            long? deadline = null;
            Value? onTimeout = null;
            List<EntityId>? about = null;
            foreach (var pair in keywords)
            {
                switch (pair.Key)
                {
                    case "choices":
                        if (!(pair.Value is ListValue items) || items.Items.Count == 0)
                            throw new ScriptRuntimeException(Codes.RunType, "choices= of ask() must be a non-empty list",
                                PureOps.ListHint(pair.Value) ?? "write " + usage);
                        for (int i = 0; i < items.Items.Count; i++)
                            choices.Add(Choice("choice " + i.ToString(CultureInfo.InvariantCulture) + " of ask()", items.Items[i]));
                        hasChoices = true;
                        break;
                    case "responses":
                        if (!(pair.Value is ListValue names) || names.Items.Count == 0)
                            throw new ScriptRuntimeException(Codes.RunType,
                                "responses= of ask() must be a non-empty list of command names", "write " + usage);
                        foreach (var item in names.Items)
                        {
                            if (!(item is StrValue name))
                                throw new ScriptRuntimeException(Codes.RunType,
                                    "responses= of ask() takes command names (str), not " + item.KindName,
                                    "write responses=[\"Pass\", \"React\"]");
                            string id = ResponseCommand(mod, name.Value);
                            if (responses.Contains(id)) continue;
                            responses.Add(id);
                            responseNames.Add(name.Value);
                        }
                        break;
                    case "hint":
                        if (pair.Value is StrValue h) hint = h.Value;
                        else if (!(pair.Value is NoneValue))
                            throw new ScriptRuntimeException(Codes.RunType, "hint= of ask() must be a str, not " + pair.Value.KindName,
                                "the hint names how the UI should ask, e.g. \"map_click\" (spec-gaps G137)");
                        break;
                    case "data":
                        if (!(pair.Value is DictValue) && !(pair.Value is NoneValue))
                            throw new ScriptRuntimeException(Codes.RunType, "data= of ask() must be a dict, not " + pair.Value.KindName,
                                "write data={\"amount\": 2} (spec-gaps G137)");
                        data = Savable("data= of ask()", pair.Value);
                        break;
                    case "blocks_time":
                        if (!(pair.Value is BoolValue b))
                            throw new ScriptRuntimeException(Codes.RunType,
                                "blocks_time= of ask() must be True or False, not " + pair.Value.KindName,
                                "True stops time until it is answered (spec-gaps G159)");
                        blocksTime = b.Value;
                        break;
                    case "deadline":
                        if (!(pair.Value is IntValue n) || n.Value < 1)
                            throw new ScriptRuntimeException(Codes.RunType,
                                "deadline= of ask() must be a number of ticks of at least 1, not " + Show(pair.Value),
                                "deadline=3 closes it 3 ticks after it opens; for a fixed tick write deadline=tick - now() (spec-gaps G160)");
                        deadline = n.Value;
                        break;
                    case "about":
                        about = About("ask", pair.Value);
                        break;
                    case "on_timeout":
                        onTimeout = pair.Value;
                        break;
                    default:
                        throw new ScriptRuntimeException(Codes.RunScript, "ask() has no argument '" + pair.Key + "'",
                            Suggest.Hint(pair.Key, new[] { "choices", "responses", "hint", "data", "blocks_time", "deadline", "on_timeout", "about" }));
                }
            }
            long? timeoutChoice = null, timeoutResponse = null;
            if (onTimeout != null) TimeoutAnswer(mod, onTimeout, deadline, choices, responses, out timeoutChoice, out timeoutResponse);
            else if (deadline != null)
                throw new ScriptRuntimeException(Codes.RunScript,
                    "ask() has a deadline but no on_timeout=, so nothing says what happens when the deadline passes",
                    "add on_timeout= with the choice or response to give then, e.g. a choice that does nothing (spec-gaps G162)");
            if (!hasChoices && responses.Count == 0)
                throw new ScriptRuntimeException(Codes.RunScript, "ask() needs choices=[...] or responses=[...]", "write " + usage);
            if (blocksTime == true && deadline != null)
                throw new ScriptRuntimeException(Codes.RunScript,
                    "ask() has blocks_time=True and a deadline, but time stops while it is open, so the deadline is never reached",
                    "drop blocks_time=True to let time flow until the deadline (spec X2, spec-gaps G159)");
            return new AskPayload(who, title, choices, responses, responseNames, hint, data)
            {
                BlocksTime = blocksTime ?? deadline == null,
                DeadlineIn = deadline,
                TimeoutChoice = timeoutChoice,
                TimeoutResponse = timeoutResponse,
                About = about,
            };
        }

        /// <summary>
        /// <c>on_timeout=</c>이 가리키는 선택지 번호나 응답 번호 (G161). 선택지 값(같은 값이 여럿이면 앞의 것)이나 응답 이름이어야 하고,
        /// 둘 다에 맞으면 어느 쪽인지 모호하다. 마감이 없으면 닿지 않는다.
        /// </summary>
        private void TimeoutAnswer(string mod, Value onTimeout, long? deadline, List<OptionValue> choices, List<string> responses,
            out long? choice, out long? response)
        {
            choice = null;
            response = null;
            if (deadline == null)
                throw new ScriptRuntimeException(Codes.RunScript, "ask() has on_timeout= but no deadline, so it never times out",
                    "add deadline=n, the number of ticks before the default answer is given (spec-gaps G160, G161)");
            var value = Savable("on_timeout= of ask()", onTimeout);
            for (int i = 0; i < choices.Count && choice == null; i++)
                if (PureOps.ValuesEqual(choices[i].Value, value)) choice = i;
            if (value is StrValue name)
            {
                string id = name.Value.IndexOf('.') >= 0 ? name.Value : mod + "." + name.Value;
                int at = responses.IndexOf(id);
                if (at >= 0) response = at;
            }
            if (choice != null && response != null)
                throw new ScriptRuntimeException(Codes.RunScript,
                    "on_timeout=\"" + ((StrValue)value).Value + "\" of ask() is both a choice and a response, so the default answer is unclear",
                    "rename the choice or the response command (spec-gaps G161)");
            if (choice == null && response == null)
            {
                var names = new List<string>();
                foreach (var c in choices) if (c.Value is StrValue s) names.Add(s.Value);
                foreach (var r in responses) names.Add(r.Substring(r.IndexOf('.') + 1));
                throw new ScriptRuntimeException(Codes.RunScript,
                    "on_timeout= of ask() must be one of its choices or responses, not " + Show(value),
                    value is StrValue missing ? Suggest.Hint(missing.Value, names) : "the default answer is given when the deadline passes (spec-gaps G161)");
            }
        }

        /// <summary>응답 커맨드 이름을 커맨드 id로: 점이 없으면 부른 함수의 모드에서 찾는다. 자유 커맨드는 답이 될 수 없다.</summary>
        private string ResponseCommand(string mod, string name)
        {
            string id = name.IndexOf('.') >= 0 ? name : mod + "." + name;
            if (!_commands.TryGetValue(id, out var decl))
            {
                throw new ScriptRuntimeException(Codes.RunUnknownName, "ask() got responses=['" + name + "'], but there is no command '" + id + "'",
                    Suggest.Hint(id, _commands.Keys));
            }
            if (decl.Free)
            {
                throw new ScriptRuntimeException(Codes.RunScript,
                    "ask() got responses=['" + name + "'], but " + id + " is a free command, so it cannot answer a question",
                    "drop free=True from it, or submit it on its own (spec S6)");
            }
            return id;
        }

        /// <summary>선택지 하나를 저장할 수 있는 option으로 (G271): 맨 값은 감싸고, option은 칸마다 저장할 수 있는 값으로 바꾼다.</summary>
        private OptionValue Choice(string display, Value item)
        {
            if (!(item is OptionValue o)) return new OptionValue(Savable(display, item));
            return new OptionValue(Savable(display, o.Value),
                o.Label == null ? null : Savable("label= of " + display, o.Label),
                o.Desc == null ? null : Savable("desc= of " + display, o.Desc),
                o.Style,
                o.Data == null ? null : Savable("data= of " + display, o.Data));
        }

        /// <summary>질문에 담아 저장할 수 있는 값으로 (spec S2 표: Request 값은 저장되므로 정규형). 엔티티는 id로 바꾼다.</summary>
        private Value Savable(string display, Value value)
        {
            switch (value)
            {
                case NoneValue _:
                case IntValue _:
                case FixedValue _:
                case BoolValue _:
                case StrValue _:
                    return value;
                case IEntityReference e:
                    return new EntityValue(e.EntityId);
                case TextValue t:
                    var args = new SortedDictionary<string, Value>(StringComparer.Ordinal);
                    foreach (var pair in t.Args) args[pair.Key] = Savable(display, pair.Value);
                    return new TextValue(t.Key, args);
                case ListValue list:
                    var items = new List<Value>(list.Items.Count);
                    foreach (var item in list.Items) items.Add(Savable(display, item));
                    return new ListValue(items);
                case DictValue dict:
                    var entries = new List<DictItem>(dict.Items.Count);
                    foreach (var item in dict.Items) entries.Add(new DictItem(item.Key, Savable(display, item.Value), item.Span));
                    return new DictValue(entries);
            }
            throw new ScriptRuntimeException(Codes.RunType, display + " cannot hold " + value.KindName,
                "a question is saved, so it holds numbers, str, text(...), entities and lists or dicts of them");
        }

        /// <summary><c>ask</c>가 실행기에 넘기는 질문의 내용. 실행기는 이것을 들고 멈추기만 한다.</summary>
        private sealed class AskPayload : Value
        {
            public AskPayload(IReadOnlyList<EntityId> who, Value title, IReadOnlyList<OptionValue> choices,
                IReadOnlyList<string> responses, IReadOnlyList<string> responseNames, string? hint, Value data)
            {
                Who = who;
                Title = title;
                Choices = choices;
                Responses = responses;
                ResponseNames = responseNames;
                Hint = hint;
                Data = data;
            }

            public IReadOnlyList<EntityId> Who { get; }
            public Value Title { get; }
            public IReadOnlyList<OptionValue> Choices { get; }
            public IReadOnlyList<string> Responses { get; }
            public IReadOnlyList<string> ResponseNames { get; }
            public string? Hint { get; }
            public Value Data { get; }
            public bool BlocksTime { get; set; } = true;

            /// <summary>열린 뒤 몇 틱 뒤가 마감인가 (G160). 질문이 열릴 때 틱으로 굳는다.</summary>
            public long? DeadlineIn { get; set; }

            public long? TimeoutChoice { get; set; }
            public long? TimeoutResponse { get; set; }

            /// <summary><c>about=</c>으로 적은 대상 (G165). 적지 않았으면 null.</summary>
            public List<EntityId>? About { get; set; }

            public override string KindName => "question";
        }

        /// <summary>
        /// <c>ask</c>가 돌려주는 값 (G138): <c>r.response</c>·<c>r.index</c>·<c>r.choice</c>·<c>r.by</c>, 그리고 응답 본문이 끝까지 가지 못하고
        /// 버려졌는가 <c>r.interrupted</c> (G243).
        /// </summary>
        private sealed class AnswerValue : Value, IRuntimeAttributes, IValueEquality
        {
            /// <summary>같은 응답·번호·선택지·답한 엔티티·끊김이면 같다 (G142).</summary>
            public bool ValueEquals(Value other) =>
                other is AnswerValue a && a._response == _response && a._index == _index && a._by == _by &&
                a._interrupted == _interrupted &&
                (a._choice == null ? _choice == null : _choice != null && PureOps.ValuesEqual(a._choice, _choice));

            private readonly World _world;
            private readonly string? _response;
            private readonly long? _index;
            private readonly Value? _choice;
            private readonly EntityId? _by;
            private readonly bool _interrupted;

            public AnswerValue(World world, string? response, long? index, Value? choice, EntityId? by, bool interrupted = false)
            {
                _world = world;
                _response = response;
                _index = index;
                _choice = choice;
                _by = by;
                _interrupted = interrupted;
            }

            public override string KindName => "answer";

            public string? Response => _response;
            public long? Index => _index;
            public Value? Choice => _choice;
            public EntityId? By => _by;
            public bool Interrupted => _interrupted;

            public Value GetAttribute(string name)
            {
                switch (name)
                {
                    case "response": return _response == null ? NoneValue.Instance : new StrValue(_response);
                    case "index": return _index == null ? NoneValue.Instance : new IntValue(_index.Value);
                    case "choice": return _choice == null ? NoneValue.Instance : _world.ToScript(_choice);
                    case "by": return _by == null ? NoneValue.Instance : _world.ToScript(new EntityValue(_by.Value));
                    case "interrupted": return _interrupted ? BoolValue.True : BoolValue.False;
                }
                throw new ScriptRuntimeException(Codes.RunScript, "an answer has no attribute '" + name + "'",
                    Suggest.Hint(name, new[] { "response", "index", "choice", "by", "interrupted" }) ??
                    "it has response, index, choice, by and interrupted");
            }
        }
    }
}
