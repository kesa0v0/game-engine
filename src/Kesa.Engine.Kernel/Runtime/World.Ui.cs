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

            /// <summary>본문이 <c>ui.…</c>를 읽었나 (화면 함수의 영수증, G279).</summary>
            public bool ReadUi { get; set; }
        }

        /// <summary>UI 동작 id들 (<c>모드id.함수이름</c>, 이름순).</summary>
        public IEnumerable<string> UiActions => _uiActions.Keys;

        /// <summary>이 월드의 스키마로 연결 하나의 UI 저장소를 새로 만든다. 모든 칸이 기본값이다 (G279).</summary>
        public UiState NewUiState() => new UiState(_schema);

        private CommandDecl FindUiAction(string action)
        {
            if (_uiActions.TryGetValue(action, out var decl)) return decl;
            throw new WorldException(Codes.RunUnknownName, "there is no UI action '" + action + "'",
                _commands.ContainsKey(action)
                    ? "'" + action + "' is a command; submit it, or call a @ui_action that submits it"
                    : Suggest.Hint(action, _uiActions.Keys));
        }

        internal ActResult ActAs(WorldView viewer, UiState ui, string action, IReadOnlyDictionary<string, Value> args)
        {
            NoHostWrites("Act");
            if (!ui.Belongs(_schema)) throw new ArgumentException("this UiState was made by another world", nameof(ui));
            var decl = FindUiAction(action);
            if (_act != null) throw new InvalidOperationException("World.Act cannot be called while another UI action runs");

            var run = new ActRun(ui.Draft());
            var savedViewer = _viewer;
            _viewer = viewer; // 인자 검사도 보는 쪽으로: 숨긴 엔티티는 죽은 것과 같다 (G198)
            _act = run;
            PreconditionReport report;
            SortedDictionary<string, Value> checkedArgs;
            try
            {
                var rejections = new List<ArgumentRejection>();
                checkedArgs = CheckArguments(decl, args, rejections);
                if (rejections.Count > 0) return new ActResult(action, ActOutcome.ArgumentsRejected) { ArgumentRejections = rejections };
                report = RunUiAction(decl, checkedArgs);
            }
            catch (WorldException e)
            {
                return new ActResult(action, ActOutcome.ScriptError) { Error = e };
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

        // ---- 버튼 확인과 나열 (G280) ----

        /// <summary>
        /// UI 동작을 누르지 않고 본다: 보는 쪽으로, 버릴 사본의 <c>ui</c>로 돈다. 회색 버튼 조건은 대개 <c>ui.…</c>를 읽는다
        /// (<c>require(ui.Selection.unit is not None, …)</c>). 아무것도 바꾸지 않는다.
        /// </summary>
        private T Peek<T>(WorldView viewer, UiState ui, string action, Func<CommandDecl, T> run)
        {
            if (!ui.Belongs(_schema)) throw new ArgumentException("this UiState was made by another world", nameof(ui));
            var decl = FindUiAction(action);
            if (_act != null) throw new InvalidOperationException("a UI action cannot be checked while another UI action runs");
            var savedViewer = _viewer;
            _viewer = viewer;
            _act = new ActRun(ui.Draft());
            try
            {
                return run(decl);
            }
            finally
            {
                _viewer = savedViewer;
                _act = null;
            }
        }

        /// <summary>화면 함수를 연결의 UI 기록과 함께 부른다 (G279·G280). 기록은 읽기만 한다(쓰는 함수는 거절된다).</summary>
        internal CallResult CallAs(WorldView viewer, UiState ui, string function, Value[] arguments)
        {
            if (!ui.Belongs(_schema)) throw new ArgumentException("this UiState was made by another world", nameof(ui));
            if (_act != null) throw new InvalidOperationException("a view function cannot be called while a UI action runs");
            var run = new ActRun(ui.Draft());
            long version = ui.Version;
            var savedViewer = _viewer;
            _viewer = viewer;
            _act = run;
            try
            {
                var result = Call(function, arguments, withUi: true);
                return run.ReadUi ? result.WithUi(ui, version) : result;
            }
            finally
            {
                _viewer = savedViewer;
                _act = null;
            }
        }

        internal PreconditionReport CheckActAs(WorldView viewer, UiState ui, string action, IReadOnlyDictionary<string, Value> args) =>
            Peek(viewer, ui, action, decl =>
            {
                var rejections = new List<ArgumentRejection>();
                var checkedArgs = CheckArguments(decl, args, rejections);
                if (rejections.Count > 0) return new PreconditionReport(decl.Id, rejections, Array.Empty<RequireOutcome>());
                return EvaluateRequires(decl, checkedArgs);
            });

        internal CandidateList ActCandidatesAs(WorldView viewer, UiState ui, string action, IReadOnlyDictionary<string, Value> args,
            string parameter) =>
            Peek(viewer, ui, action, decl => Candidates(decl, args, parameter));

        internal LegalMoveList ActMovesAs(WorldView viewer, UiState ui, string action, IReadOnlyDictionary<string, Value> args, int max,
            Func<IReadOnlyDictionary<string, Value>, bool> keep)
        {
            if (max < 1) throw new WorldException(Codes.RunRange, "max must be at least 1, not " + max);
            return Peek(viewer, ui, action, decl => LegalMoves(decl, args, max, keep));
        }

        /// <summary>
        /// UI 정의를 읽을 때 커맨드 버튼(<c>command=</c>)을 확인한다 (G280): 있는 커맨드이고 적은 매개변수 이름이 모두 있으면 null, 아니면
        /// 이유 문장("혹시 …?" 포함). <see cref="CheckCall"/>과 짝이다.
        /// </summary>
        public string? CheckCommand(string command, IEnumerable<string>? parameters = null)
        {
            if (!_commands.TryGetValue(command, out var decl))
            {
                string? hint = _uiActions.ContainsKey(command) ? "'" + command + "' is a @ui_action, not a command" : Suggest.Hint(command, _commands.Keys);
                return "there is no command '" + command + "'" + (hint == null ? "" : " (" + hint + ")");
            }
            return CheckParameters(decl, parameters);
        }

        /// <summary>UI 동작 버튼(<c>ui_action=</c>)을 확인한다 (G280). <see cref="CheckCommand"/>와 같은 모양이다.</summary>
        public string? CheckUiAction(string action, IEnumerable<string>? parameters = null)
        {
            try
            {
                return CheckParameters(FindUiAction(action), parameters);
            }
            catch (WorldException e)
            {
                return e.Hint == null ? e.Message : e.Message + " (" + e.Hint + ")";
            }
        }

        private static string? CheckParameters(CommandDecl decl, IEnumerable<string>? parameters)
        {
            if (parameters == null) return null;
            var names = new List<string>();
            foreach (var p in decl.Parameters) names.Add(p.Name);
            foreach (var name in parameters)
            {
                if (names.Contains(name)) continue;
                string? hint = Suggest.Hint(name, names);
                return decl.Id + " has no parameter '" + name + "'" + (hint == null ? "" : " (" + hint + ")");
            }
            return null;
        }

        /// <summary>UI 동작 밖의 계산(<c>derived</c>, 보기 정책)을 돌린다: 그 안에서는 <c>ui</c>가 없다 (게임 값은 화면에 기대지 않는다, G282).</summary>
        private T OutsideAct<T>(Func<T> run)
        {
            var saved = _act;
            var savedBot = _botRun;
            _act = null;
            _botRun = null;
            try
            {
                return run();
            }
            finally
            {
                _act = saved;
                _botRun = savedBot;
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
            if (submission.Command == PickCommand) return DeliverPick(issuer, submission.Arguments);
            if (!_commands.TryGetValue(submission.Command, out var decl))
            {
                throw new WorldException(Codes.RunUnknownName, "there is no command '" + submission.Command + "'",
                    Suggest.Hint(submission.Command, _commands.Keys));
            }
            if (decl.Free) return new DeliveryResult(Submit(issuer, submission.Command, submission.Arguments), null, null);

            var accepting = AcceptingRequests(issuer, submission.Command);
            if (accepting.Count == 1)
                return new DeliveryResult(null, Respond(issuer, accepting[0].Id, submission.Command, submission.Arguments), null);
            return new DeliveryResult(null, null, NotDeliverable(issuer, submission.Command, accepting.Count));
        }

        /// <summary>
        /// 선택지 답 <c>submit("engine.pick", choice=n, by=e)</c> (G379): 이 연결이 답할 수 있는 선택지 질문이 하나면 거기에 답한다.
        /// <c>by</c>를 빼면 그 질문에서 이 연결이 답할 수 있는 엔티티가 하나여야 한다. 아니면 이유를 붙여 거절한다.
        /// </summary>
        private DeliveryResult DeliverPick(string issuer, IReadOnlyDictionary<string, Value> args)
        {
            EntityId? by = null;
            if (args.TryGetValue(PickBy, out var given))
            {
                if (!(given is IEntityReference e))
                    return new DeliveryResult(null, null, PickCommand + " needs by= to be an entity, not " + given.KindName);
                by = e.EntityId;
            }
            if (!args.TryGetValue(PickChoice, out var choice) || !(choice is IntValue index))
                return new DeliveryResult(null, null, PickCommand + " needs choice= with the index of the choice");

            Request? found = null;
            var answerers = new List<EntityId>();
            int questions = 0;
            foreach (var q in OpenRequests)
            {
                if (q.Choices.Count == 0) continue;
                var mine = new List<EntityId>();
                foreach (var who in q.Who)
                    if ((by == null || who == by) && IsAlive(who) && MayAnswer(issuer, who)) mine.Add(who);
                if (mine.Count == 0) continue;
                questions++;
                found = q;
                answerers = mine;
            }
            if (questions != 1)
                return new DeliveryResult(null, null, questions == 0
                    ? "no open question with choices that " + issuer + " can answer"
                    : questions + " open questions with choices can be answered by " + issuer + ", so it is not clear which one this answers");
            if (answerers.Count != 1)
                return new DeliveryResult(null, null, answerers.Count + " entities of " + issuer + " can answer request " + found!.Id +
                                                      "; say which with by=");
            return new DeliveryResult(null, Answer(issuer, found!.Id, answerers[0], index.Value), null);
        }

        /// <summary>이 연결이 답할 수 있고 그 응답 커맨드를 받는 열린 질문들.</summary>
        private List<Request> AcceptingRequests(string issuer, string command)
        {
            var accepting = new List<Request>();
            foreach (var q in OpenRequests)
            {
                if (IndexOf(q.Responses, command) < 0) continue;
                foreach (var who in q.Who)
                {
                    if (!IsAlive(who) || !MayAnswer(issuer, who)) continue;
                    accepting.Add(q);
                    break;
                }
            }
            return accepting;
        }

        private static string NotDeliverable(string issuer, string command, int accepting) =>
            accepting == 0
                ? command + " is not a free command, and no open question that " + issuer + " can answer accepts it"
                : command + " is accepted by " + accepting + " open questions that " + issuer +
                  " can answer, so it is not clear which one it answers";

        /// <summary>
        /// <see cref="Deliver"/>를 하지 않고 같은 길의 입구만 미리 본다 (회색 버튼, spec-gaps G233·G283): 자유 커맨드는 <c>acts_for</c>로 이 연결이
        /// 낼 수 있는가, 응답 커맨드는 이 연결이 답할 수 있고 그 커맨드를 받는 열린 질문이 하나이고 그 질문의 대상으로 답할 수 있는가.
        /// 낼 수 있으면 null, 아니면 이유(개발자용 영어). 인자는 사전 검사를 통과한 것이어야 한다.
        /// </summary>
        internal string? WhyNotDeliverable(string issuer, string command, IReadOnlyDictionary<string, Value> args, List<Diagnostic> diagnostics)
        {
            var decl = FindCommand(command);
            if (decl.Free)
                return MayIssue(issuer, command, args, diagnostics) ? null : "'" + issuer + "' cannot issue " + command + " with these arguments";
            var accepting = AcceptingRequests(issuer, command);
            if (accepting.Count != 1) return NotDeliverable(issuer, command, accepting.Count);
            try
            {
                if (!(ActsFor(decl, Sorted(args)) is EntityValue by))
                    return "computing acts_for of " + decl.Id + ": a response must come from an entity, not None";
                return Gate(issuer, accepting[0], by.Id)?.Message;
            }
            catch (WorldException e)
            {
                return e.Message;
            }
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
            table.Add(PureBuiltins.BotName, new UiRoot(this, bot: true));
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
            var submissions = _act?.Submissions ?? _botRun?.Submissions ?? throw OutsideUi("submit()");
            if (positional.Count != 1 || !(positional[0] is StrValue name))
            {
                throw new ScriptRuntimeException(Codes.RunType,
                    "submit() takes the command name and then its arguments by name",
                    "write submit(\"Attack\", target=enemy) (spec-gaps G278)");
            }
            string id = name.Value.IndexOf('.') >= 0 ? name.Value : mod + "." + name.Value;
            if (!_commands.ContainsKey(id) && id != PickCommand)
            {
                throw new ScriptRuntimeException(Codes.RunUnknownName, "submit() names '" + name.Value + "', but there is no command '" + id + "'",
                    _uiActions.ContainsKey(id) ? "a @ui_action runs on the screen; submit a @command" : Suggest.Hint(id, _commands.Keys));
            }
            var args = new SortedDictionary<string, Value>(StringComparer.Ordinal);
            foreach (var pair in keywords) args[pair.Key] = ToPlain("argument '" + pair.Key + "' of submit()", pair.Value);
            submissions.Add(new UiSubmission(id, args));
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

        private bool Gone(EntityValue e)
        {
            Read(e.Id.Value, AliveRead); // 살았는지에 따라 읽는 값이 달라진다: 영수증에 든다 (G91·G246과 같다)
            return !IsAlive(e.Id) || HiddenFromViewer(e.Id.Value);
        }

        /// <summary>스크립트의 <c>ui</c>(봇이면 <c>bot</c>, G371): 기록 이름으로 <see cref="UiRecord"/>를 준다.</summary>
        private sealed class UiRoot : Value, IRuntimeAttributes
        {
            private readonly World _world;
            private readonly bool _bot;

            public UiRoot(World world, bool bot = false)
            {
                _world = world;
                _bot = bot;
            }

            public override string KindName => _bot ? "bot" : "ui";

            public Value GetAttribute(string name)
            {
                if (_bot ? _world._botRun == null : _world._act == null) throw _bot ? OutsideBot("bot") : OutsideUi("ui");
                if (_bot ? _world._schema.TryGetBotState(name, out var state) : _world._schema.TryGetUiState(name, out state))
                    return new UiRecord(_world, state);
                var names = new List<string>();
                foreach (var s in _bot ? _world._schema.BotStates : _world._schema.UiStates) names.Add(s.Name);
                throw new ScriptRuntimeException(Codes.RunUnknownName, "there is no " + (_bot ? "bot" : "UI") + " state '" + name + "'",
                    Suggest.Hint(name, names));
            }
        }

        /// <summary>
        /// <c>ui.Selection</c>(<c>bot.Mind</c>): 칸을 읽고 쓴다. 쓰기는 동작(생각하기)의 사본에 들어가고 성공하면 저장소에 들어간다.
        /// </summary>
        private sealed class UiRecord : Value, IRuntimeAttributes, IUiRecord
        {
            private readonly World _world;
            private readonly UiStateType _state;

            public UiRecord(World world, UiStateType state)
            {
                _world = world;
                _state = state;
            }

            private string Noun => _state.IsBot ? "bot state" : "UI state";

            public override string KindName => (_state.IsBot ? "bot state " : "UI state ") + _state.Name;

            private MemberDecl Member(string name)
            {
                if (_state.TryGetMember(name, out var member)) return member;
                var names = new List<string>();
                foreach (var m in _state.Members) names.Add(m.Name);
                throw new ScriptRuntimeException(Codes.RunUnknownName,
                    (_state.IsBot ? "Bot state '" : "UI state '") + _state.Name + "' has no field '" + name + "'", Suggest.Hint(name, names));
            }

            private SortedDictionary<string, Value> Fields()
            {
                if (_state.IsBot)
                {
                    if (_world._botRun == null) throw OutsideBot("bot");
                    return _world._botRun.Draft[_state.Name];
                }
                if (_world._act == null) throw OutsideUi("ui");
                return _world._act.Draft[_state.Name];
            }

            public Value GetAttribute(string name)
            {
                Member(name);
                var fields = Fields();
                if (!_state.IsBot) _world._act!.ReadUi = true;
                return _world.UiRead(fields[name]);
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
                    throw new ScriptRuntimeException(Codes.RunType, rejections[0].Message, _state.IsBot ? "spec-gaps G371" : "spec-gaps G279");
                fields[name] = fitted;
            }
        }
    }
}
