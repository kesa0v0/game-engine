using System;
using System.Collections.Generic;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Schema;
using Kesa.Engine.Script;

namespace Kesa.Engine.Runtime
{
    /// <summary>
    /// UI 동작 (spec S10 2·3항, spec-gaps G276~G284): 호스트가 연결의 <see cref="UiState"/>와 함께 <c>@ui_action</c>을 부르고
    /// (<see cref="WorldView.Act"/>), 본문이 적어 둔 <c>submit</c>을 <see cref="Deliver"/>로 낸다. UI 동작은 게임을 바꾸지 않는다.
    /// </summary>
    public sealed partial class World
    {
        private readonly IReadOnlyDictionary<string, CommandDecl> _uiActions =
            new SortedDictionary<string, CommandDecl>(StringComparer.Ordinal);

        /// <summary>도는 중인 UI 동작. 없으면 스크립트의 <c>ui</c>·<c>submit</c>은 실행 오류다 (G282의 실행 중 안전망).</summary>
        private ActRun? _act;

        private sealed class ActRun
        {
            public ActRun(SortedDictionary<string, SortedDictionary<string, Value>> draft) => Draft = draft;

            public SortedDictionary<string, SortedDictionary<string, Value>> Draft { get; }
            public List<UiSubmission> Submissions { get; } = new List<UiSubmission>();
        }

        /// <summary>UI 동작 id들 (<c>모드id.함수이름</c>, 이름순).</summary>
        public IEnumerable<string> UiActions => _uiActions.Keys;

        /// <summary>이 월드의 스키마로 연결 하나의 UI 저장소를 새로 만든다. 모든 칸이 기본값이다 (G279).</summary>
        public UiState NewUiState() => new UiState(_schema);

        internal ActResult ActAs(WorldView viewer, UiState ui, string action, IReadOnlyDictionary<string, Value> args)
        {
            NoHostWrites("Act");
            if (!ui.Belongs(_schema)) throw new ArgumentException("this UiState was made by another world", nameof(ui));
            if (!_uiActions.TryGetValue(action, out var decl))
            {
                throw new WorldException(Codes.RunUnknownName, "there is no UI action '" + action + "'",
                    _commands.ContainsKey(action)
                        ? "'" + action + "' is a command; submit it, or call a @ui_action that submits it"
                        : Suggest.Hint(action, _uiActions.Keys));
            }
            if (_act != null) throw new InvalidOperationException("World.Act cannot be called while another UI action runs");

            var rejections = new List<ArgumentRejection>();
            var checkedArgs = CheckArguments(decl, args, rejections);
            if (rejections.Count > 0) return new ActResult(action, ActOutcome.ArgumentsRejected) { ArgumentRejections = rejections };

            var run = new ActRun(ui.Draft());
            var savedViewer = _viewer;
            _viewer = viewer;
            _act = run;
            PreconditionReport report;
            try
            {
                report = RunUiAction(decl, checkedArgs);
            }
            catch (WorldException e)
            {
                return new ActResult(action, ActOutcome.ScriptError) { Arguments = checkedArgs, Error = e };
            }
            finally
            {
                _viewer = savedViewer;
                _act = null;
            }
            if (!report.Passed)
                return new ActResult(action, ActOutcome.PreconditionsFailed) { Arguments = checkedArgs, Preconditions = report };
            ui.Commit(run.Draft);
            return new ActResult(action, ActOutcome.Succeeded)
            {
                Arguments = checkedArgs, Preconditions = report, Submissions = run.Submissions,
            };
        }

        private PreconditionReport RunUiAction(CommandDecl decl, SortedDictionary<string, Value> checkedArgs)
        {
            var scriptArgs = new SortedDictionary<string, Value>(StringComparer.Ordinal);
            foreach (var pair in checkedArgs) scriptArgs[pair.Key] = ToScript(pair.Value);
            bool outermost = _meter == null;
            if (outermost) _meter = new StepMeter(_actionStepBudget);
            try
            {
                var raw = Evaluator.RunUiAction(decl.Function, scriptArgs, decl.Span, _meter);
                var requires = new List<RequireOutcome>(raw.Count);
                foreach (var r in raw)
                {
                    var plain = new SortedDictionary<string, Value>(StringComparer.Ordinal);
                    foreach (var pair in r.Args) plain[pair.Key] = ToPlain("argument '" + pair.Key + "' of require()", pair.Value);
                    requires.Add(r.WithArgs(plain));
                }
                return new PreconditionReport(decl.Id, requires);
            }
            catch (ScriptRuntimeException e)
            {
                throw ToWorldException(e, "the UI action " + decl.Id);
            }
            finally
            {
                if (outermost) _meter = null;
            }
        }

        /// <summary>UI 동작 밖의 계산(<c>derived</c>, 보기 정책)을 돌린다: 그 안에서는 <c>ui</c>가 없다 (게임 값은 화면에 기대지 않는다, G282).</summary>
        private T OutsideAct<T>(Func<T> run)
        {
            var saved = _act;
            _act = null;
            try
            {
                return run();
            }
            finally
            {
                _act = saved;
            }
        }

        // ---- 커맨드 내기 (G283) ----

        /// <summary>
        /// UI 동작이 적어 둔 <c>submit</c> 하나를 낸다 (spec-gaps G283). 자유 커맨드면 <see cref="Submit"/>이다. 아니면 이 연결이 답할 수 있는
        /// 열린 질문 가운데 그 커맨드를 받는 것을 찾아, 하나면 거기에 <see cref="Respond"/>하고 없거나 여럿이면 이유를 붙여 거절한다.
        /// 없는 커맨드 id만 호출자 오류(<see cref="WorldException"/>)다.
        /// </summary>
        public DeliveryResult Deliver(string issuer, UiSubmission submission)
        {
            NoHostWrites("Deliver");
            if (!_commands.TryGetValue(submission.Command, out var decl))
            {
                throw new WorldException(Codes.RunUnknownName, "there is no command '" + submission.Command + "'",
                    Suggest.Hint(submission.Command, _commands.Keys));
            }
            if (decl.Free) return new DeliveryResult(Submit(issuer, submission.Command, submission.Arguments), null, null);

            var accepting = new List<Request>();
            foreach (var q in OpenRequests)
            {
                if (IndexOf(q.Responses, submission.Command) < 0) continue;
                foreach (var who in q.Who)
                {
                    if (!IsAlive(who) || !MayAnswer(issuer, who)) continue;
                    accepting.Add(q);
                    break;
                }
            }
            if (accepting.Count == 1)
                return new DeliveryResult(null, Respond(issuer, accepting[0].Id, submission.Command, submission.Arguments), null);
            return new DeliveryResult(null, null, accepting.Count == 0
                ? submission.Command + " is not a free command, and no open question that " + issuer + " can answer accepts it"
                : submission.Command + " is accepted by " + accepting.Count + " open questions that " + issuer +
                  " can answer, so it is not clear which one it answers");
        }

        private static int IndexOf(IReadOnlyList<string> items, string item)
        {
            for (int i = 0; i < items.Count; i++)
                if (items[i] == item) return i;
            return -1;
        }

        // ---- 스크립트의 ui와 submit (G278·G279·G282) ----

        private void AddUiBuiltins(SortedDictionary<string, Value> table)
        {
            table.Add(PureBuiltins.UiName, new UiRoot(this));
            table.Add(PureBuiltins.SubmitName,
                new PureBuiltin(PureBuiltins.SubmitName, (p, k) => SubmitBuiltin(_evaluator?.ScriptMod ?? "", p, k))
                {
                    WritesUi = true,
                    CallerBody = SubmitBuiltin,
                });
        }

        private static ScriptRuntimeException OutsideUi(string what) =>
            new ScriptRuntimeException(Codes.RunNotWritable, what + " can only be used while a @ui_action runs",
                "UI state is different on every screen and is not saved, so game code cannot use it (spec-gaps G282)");

        private Value SubmitBuiltin(string mod, IReadOnlyList<Value> positional, IReadOnlyDictionary<string, Value> keywords)
        {
            if (_act == null) throw OutsideUi("submit()");
            if (positional.Count != 1 || !(positional[0] is StrValue name))
            {
                throw new ScriptRuntimeException(Codes.RunType,
                    "submit() takes the command name and then its arguments by name",
                    "write submit(\"Attack\", target=enemy) (spec-gaps G278)");
            }
            string id = name.Value.IndexOf('.') >= 0 ? name.Value : mod + "." + name.Value;
            if (!_commands.ContainsKey(id))
            {
                throw new ScriptRuntimeException(Codes.RunUnknownName, "submit() names '" + name.Value + "', but there is no command '" + id + "'",
                    _uiActions.ContainsKey(id) ? "a @ui_action runs on the screen; submit a @command" : Suggest.Hint(id, _commands.Keys));
            }
            var args = new SortedDictionary<string, Value>(StringComparer.Ordinal);
            foreach (var pair in keywords) args[pair.Key] = ToPlain("argument '" + pair.Key + "' of submit()", pair.Value);
            _act.Submissions.Add(new UiSubmission(id, args));
            return NoneValue.Instance;
        }

        /// <summary>
        /// 적힌 값을 읽는 모양으로 (G279·G284): 없어진(또는 보는 쪽에게 숨긴) 엔티티는 한 칸이면 <c>None</c>이고 리스트·사전에서는 빠진다.
        /// 엔티티는 따라갈 수 있는 모양이다.
        /// </summary>
        private Value UiRead(Value stored)
        {
            switch (stored)
            {
                case EntityValue e:
                    return Gone(e) ? (Value)NoneValue.Instance : ToScript(e);
                case ListValue list:
                {
                    var items = new List<Value>(list.Items.Count);
                    foreach (var item in list.Items)
                        if (!(item is EntityValue e && Gone(e))) items.Add(UiRead(item));
                    return new ListValue(items);
                }
                case DictValue dict:
                {
                    var items = new List<DictItem>(dict.Items.Count);
                    foreach (var item in dict.Items)
                        if (!(item.Key is EntityValue k && Gone(k)) && !(item.Value is EntityValue v && Gone(v)))
                            items.Add(new DictItem(UiRead(item.Key), UiRead(item.Value), item.Span));
                    return new DictValue(items);
                }
            }
            return stored;
        }

        private bool Gone(EntityValue e) => !IsAlive(e.Id) || HiddenFromViewer(e.Id.Value);

        /// <summary>스크립트의 <c>ui</c>: 기록 이름으로 <see cref="UiRecord"/>를 준다.</summary>
        private sealed class UiRoot : Value, IRuntimeAttributes
        {
            private readonly World _world;

            public UiRoot(World world) => _world = world;

            public override string KindName => "ui";

            public Value GetAttribute(string name)
            {
                if (_world._act == null) throw OutsideUi("ui");
                if (_world._schema.TryGetUiState(name, out var state)) return new UiRecord(_world, state);
                var names = new List<string>();
                foreach (var s in _world._schema.UiStates) names.Add(s.Name);
                throw new ScriptRuntimeException(Codes.RunUnknownName, "there is no UI state '" + name + "'", Suggest.Hint(name, names));
            }
        }

        /// <summary><c>ui.Selection</c>: 칸을 읽고 쓴다. 쓰기는 UI 동작의 사본에 들어가고 동작이 성공하면 저장소에 들어간다.</summary>
        private sealed class UiRecord : Value, IRuntimeAttributes, IUiRecord
        {
            private readonly World _world;
            private readonly UiStateType _state;

            public UiRecord(World world, UiStateType state)
            {
                _world = world;
                _state = state;
            }

            public override string KindName => "UI state " + _state.Name;

            private MemberDecl Member(string name)
            {
                if (_state.TryGetMember(name, out var member)) return member;
                var names = new List<string>();
                foreach (var m in _state.Members) names.Add(m.Name);
                throw new ScriptRuntimeException(Codes.RunUnknownName, "UI state '" + _state.Name + "' has no field '" + name + "'",
                    Suggest.Hint(name, names));
            }

            private SortedDictionary<string, Value> Fields()
            {
                if (_world._act == null) throw OutsideUi("ui");
                return _world._act.Draft[_state.Name];
            }

            public Value GetAttribute(string name)
            {
                Member(name);
                return _world.UiRead(Fields()[name]);
            }

            public void SetAttribute(string name, Value value)
            {
                var member = Member(name);
                var fields = Fields();
                var plain = ToPlain(member.Display, value);
                if (member.Kind == MemberKind.Ref && plain is NoneValue)
                {
                    fields[name] = plain;
                    return;
                }
                var rejections = new List<ArgumentRejection>();
                var fitted = _world.Fit(name, member.Display, member.Spec.Type, plain, rejections);
                if (fitted == null)
                    throw new ScriptRuntimeException(Codes.RunType, rejections[0].Message, "spec-gaps G279");
                fields[name] = fitted;
            }
        }
    }
}
