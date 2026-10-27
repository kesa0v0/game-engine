using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Kesa.Engine.Bridge;
using Kesa.Engine.Text;

namespace Kesa.Engine.ConsoleHost
{
    /// <summary>
    /// 터미널 핫시트 한 판. 게임 지식 없이 호스트 경계(<see cref="IHostOperator"/>·<see cref="IHostSession"/>)만으로 화면을 만든다:
    /// 상태는 연결이 보는 엔티티를 늘어놓고, 할 수 있는 것은 열린 질문(선택지·응답 커맨드의 합법 수)과 자유 커맨드의 합법 수다.
    /// 읽기는 연결마다 그 연결의 세션으로 한다.
    /// </summary>
    public sealed class ConsoleGame
    {
        /// <summary>커맨드 하나에서 보여 줄 합법 수의 상한.</summary>
        public const int MaxMoves = 50;

        private readonly IHostOperator _host;
        /// <summary>연결마다 앉힐 Def. 그 Def의 엔티티가 생기면 앉힌다. Def가 null이면 후보가 생길 때 고르게 묻는다.</summary>
        private readonly IReadOnlyList<(string Connection, string? Def)> _wanted;

        /// <summary>이미 자리를 물은 연결. 한 번만 묻는다(앉은 진영이 죽어도 다시 묻지 않는다).</summary>
        private readonly HashSet<string> _asked = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>연결 꼬리표, 처음 나온 순서.</summary>
        private readonly IReadOnlyList<string> _seats;

        private readonly Dictionary<string, IHostSession> _sessions = new Dictionary<string, IHostSession>(StringComparer.Ordinal);

        private readonly TextReader _in;
        private readonly TextWriter _out;
        private readonly ValueText _text;

        /// <summary>모드의 <c>ui/console.txt</c> (G250). 비면 엔티티를 늘어놓는다.</summary>
        private readonly IReadOnlyList<ConsoleUi.Panel> _panels;

        /// <summary>UI 동작에 앉은 엔티티를 넣는 매개변수 이름 (콘솔의 약속, G286).</summary>
        public const string MeParameter = "me";

        public ConsoleGame(IHostOperator host, IReadOnlyList<(string Connection, string? Def)> seats,
            IReadOnlyList<ConsoleUi.Panel> panels, Translator translator, TextReader input, TextWriter output)
        {
            _host = host;
            _wanted = seats;
            _panels = panels;
            _seats = seats.Select(s => s.Connection).Distinct().ToList();
            _in = input;
            _out = output;
            _text = new ValueText(translator);
        }

        private sealed class Option
        {
            public Option(string seat, string label, Func<string> run)
            {
                Seat = seat;
                Label = label;
                Run = run;
            }

            /// <summary>이 수를 내는 자리(연결 꼬리표).</summary>
            public string Seat { get; }

            public string Label { get; }

            /// <summary>그 자리로 내고 결과를 한 줄로 돌려준다.</summary>
            public Func<string> Run { get; }
        }

        /// <summary>경계의 호출은 같은 프로세스라 이미 끝나 있다 (G327).</summary>
        private static T Wait<T>(ValueTask<T> task) => task.IsCompletedSuccessfully ? task.Result : task.AsTask().GetAwaiter().GetResult();

        private static void Wait(ValueTask task)
        {
            if (!task.IsCompletedSuccessfully) task.AsTask().GetAwaiter().GetResult();
        }

        private IHostSession Session(string seat)
        {
            if (!_sessions.TryGetValue(seat, out var session)) _sessions[seat] = session = Wait(_host.Open(seat));
            return session;
        }

        private static BridgeValue Int(long n) => BridgeValue.Of(n);

        private static BridgeValue Str(string s) => BridgeValue.Of(s);

        /// <summary>낼 수 있는 것이 없거나 입력이 끝나거나 <c>:q</c>면 끝난다.</summary>
        public void Run()
        {
            while (true)
            {
                SeatArrivals();
                if (!AskSeats()) return;
                WriteState();
                var options = Options();
                if (options.Count == 0)
                {
                    _out.WriteLine(_text.Say("no-moves"));
                    return;
                }
                for (int i = 0; i < options.Count; i++)
                    _out.WriteLine("  " + (i + 1).ToString(CultureInfo.InvariantCulture) + ") [" + options[i].Seat + "] " + options[i].Label);

                Option? picked = null;
                while (picked == null)
                {
                    _out.Write("> ");
                    string? line = _in.ReadLine();
                    if (line == null || line.Trim() == ":q") return;
                    if (int.TryParse(line.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) &&
                        n >= 1 && n <= options.Count)
                        picked = options[n - 1];
                    else
                        _out.WriteLine(_text.Say("pick-number", ("n", Int(options.Count))));
                }
                _out.WriteLine("[" + picked.Seat + "] " + picked.Run());
            }
        }

        // ---- 자리 ----

        /// <summary>
        /// 원하는 Def의 엔티티가 생겼고 아무도 앉지 않았으면 그 연결을 앉힌다 (G236의 호스트 쓰기 경로). 테스트·자동 실행용이고 모드의
        /// Def 이름을 알아야 한다. 이름 없이 고르는 길은 <see cref="AskSeats"/>.
        /// </summary>
        private void SeatArrivals()
        {
            foreach (var (connection, def) in _wanted)
            {
                if (def == null) continue;
                var seat = Wait(_host.SeatChoices()).FirstOrDefault(s => s.Entity.Def == def && s.Connection == null);
                if (seat == null) continue;
                Wait(_host.Seat(seat.Entity.Id, connection));
                _out.WriteLine("[" + connection + "] " + Seated(seat.Entity));
            }
        }

        /// <summary>
        /// 작은 로비: Def 없이 적은 연결마다, 빈 앉을 후보(<c>SeatChoices</c>, G239)가 생기면 그중 하나를 고르게 묻는다. 입력이
        /// 끝나거나 <c>:q</c>면 false.
        /// </summary>
        private bool AskSeats()
        {
            foreach (var (connection, def) in _wanted)
            {
                if (def != null || _asked.Contains(connection) || Wait(Session(connection).Seats()).Count > 0) continue;
                var free = Wait(_host.SeatChoices()).Where(s => s.Connection == null).Select(s => s.Entity).ToList();
                if (free.Count == 0) continue; // 후보가 아직 없다: 생기면 묻는다
                _asked.Add(connection);
                _out.WriteLine(_text.Say("seat-choices") + " " + string.Join("  ",
                    free.Select((e, i) => (i + 1).ToString(CultureInfo.InvariantCulture) + ") " + _text.Entity(e))));
                while (true)
                {
                    _out.Write(_text.Say("where", ("connection", Str(connection))) + " ");
                    string? line = _in.ReadLine();
                    if (line == null || line.Trim() == ":q") return false;
                    if (int.TryParse(line.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) &&
                        n >= 1 && n <= free.Count)
                    {
                        Wait(_host.Seat(free[n - 1].Id, connection));
                        _out.WriteLine("[" + connection + "] " + Seated(free[n - 1]));
                        break;
                    }
                    _out.WriteLine(_text.Say("pick-number", ("n", Int(free.Count))));
                }
            }
            return true;
        }

        private string Seated(BridgeEntity entity) => _text.Say("seated", ("entity", Str(_text.Entity(entity))));

        // ---- 상태 ----

        /// <summary>연결·엔티티마다 마지막으로 그린 줄과 그때의 도장. 도장이 그대로면 값을 다시 읽지 않는다 (G235).</summary>
        private readonly Dictionary<(string, long), (long Version, string Line)> _lines = new Dictionary<(string, long), (long, string)>();

        /// <summary>테스트용: 도장을 보지 않고 매번 다시 읽는다. 도장으로 건너뛴 화면과 견주는 데 쓴다.</summary>
        internal static bool AlwaysRedraw { get; set; }

        /// <summary>모드의 UI 파일이 있으면 그 패널들, 없으면 엔티티를 늘어놓고, 열린 질문을 덧붙인다.</summary>
        private void WriteState()
        {
            _out.WriteLine();
            _out.WriteLine(_text.Say("tick", ("tick", Int(Wait(Session(_seats[0]).Tick())))));
            if (_panels.Count > 0) WritePanels();
            else WriteEntities();
            WriteRequests();
        }

        /// <summary>열린 질문을 번호마다 한 줄. 질문의 대상은 자리마다 그 연결이 답할 수 있는 것만이라 합쳐서 보인다 (G234).</summary>
        private void WriteRequests()
        {
            var asked = new SortedDictionary<long, (BridgeRequest Request, List<BridgeEntity> Who)>();
            foreach (var seat in _seats)
            {
                foreach (var request in Wait(Session(seat).OpenRequests()))
                {
                    if (!asked.TryGetValue(request.Id, out var known)) asked[request.Id] = known = (request, new List<BridgeEntity>());
                    foreach (var who in request.Who)
                        if (!known.Who.Any(w => w.Id == who.Id)) known.Who.Add(who);
                }
            }
            foreach (var (request, who) in asked.Values)
            {
                _out.WriteLine(_text.Say("question", ("id", Int(request.Id)), ("title", Str(_text.Label(request.Title))),
                                   ("who", Str(string.Join(", ", who.Select(_text.Entity))))) +
                               (request.Data.IsNone ? "" : "  " + _text.Of(request.Data)));
            }
        }

        /// <summary>
        /// 자리마다, 그 연결이 앉은 엔티티마다 UI 파일의 함수를 그 연결의 세션에서 부른다 (G248·G250). 영수증이 그대로면 부르지 않는다
        /// (G246). 부르다 실패한 칸은 오류 줄을 보이고 나머지는 계속 그린다(G247).
        /// </summary>
        private void WritePanels()
        {
            foreach (var seat in _seats)
            {
                var session = Session(seat);
                var changed = new HashSet<string>(Wait(session.Changed()), StringComparer.Ordinal);
                foreach (var me in Wait(session.Seats()))
                {
                    _out.WriteLine("[" + seat + "] " + _text.Entity(me));
                    foreach (var panel in _panels)
                    {
                        string key = me.Id.ToString(CultureInfo.InvariantCulture) + "|" + panel.Place;
                        var drawn = (seat, me.Id, panel.Place);
                        if (AlwaysRedraw || !_drawn.TryGetValue(drawn, out var lines) || changed.Contains(key))
                            _drawn[drawn] = lines = Draw(session, key, panel, me);
                        foreach (var line in lines) _out.WriteLine(line);
                    }
                }
            }
        }

        /// <summary>(연결, 앉은 엔티티, 패널 곳)마다 그린 줄. 실패한 칸은 영수증이 없어 세션이 다시 부를 것으로 알려 준다.</summary>
        private readonly Dictionary<(string, long, string), List<string>> _drawn = new Dictionary<(string, long, string), List<string>>();

        private List<string> Draw(IHostSession session, string key, ConsoleUi.Panel panel, BridgeEntity me)
        {
            var call = Wait(session.Call(key, panel.Function, new BridgeValue[] { me }));
            if (!call.Succeeded) return new List<string> { "  " + panel.Place + ": " + call.Error };
            var lines = new List<string>();
            if (call.Value is BridgeList list)
                foreach (var item in list.Items) lines.Add("  " + _text.Of(item));
            else lines.Add("  " + _text.Of(call.Value!));
            return lines;
        }

        /// <summary>
        /// 자리마다 그 연결이 보는 엔티티를, 바뀌었을 때만(도장) 다시 읽어 늘어놓는다. 도장은 남의 엔티티를 읽는 <c>derived</c>를 담지
        /// 않으므로 <c>derived</c> 칸이 있는 엔티티는 매번 다시 읽는다 (G251).
        /// </summary>
        private void WriteEntities()
        {
            foreach (var seat in _seats)
            {
                var session = Session(seat);
                _out.WriteLine("[" + seat + "]");
                foreach (var listed in Wait(session.Entities()))
                {
                    var key = (seat, listed.Entity.Id);
                    if (AlwaysRedraw || listed.Volatile || !_lines.TryGetValue(key, out var drawn) || drawn.Version != listed.Version)
                        _lines[key] = drawn = (listed.Version, Line(Wait(session.Read(listed.Entity.Id)), listed.Entity));
                    _out.WriteLine(drawn.Line);
                }
            }
        }

        private string Line(IReadOnlyList<BridgeComponent>? components, BridgeEntity entity)
        {
            var parts = new List<string>();
            foreach (var component in components ?? Array.Empty<BridgeComponent>())
            {
                var members = component.Members.Select(m => m.Key + "=" + _text.Of(m.Value));
                parts.Add(component.Name + "(" + string.Join(", ", members) + ")");
            }
            return _text.Entity(entity) + "  " + string.Join(" ", parts);
        }

        // ---- 할 수 있는 것 ----

        /// <summary>자리마다 그 연결의 세션으로 그 자리가 낼 수 있는 것만 모은다 (G200·G232·G233).</summary>
        private List<Option> Options()
        {
            var options = new List<Option>();
            foreach (var seat in _seats)
            {
                var session = Session(seat);
                foreach (var request in Wait(session.OpenRequests()))
                {
                    // 선택지 답(engine.pick)과 응답 커맨드가 한 목록이고 어느 것이든 Respond로 낸다 (G272).
                    var responses = Wait(session.LegalResponses(request.Id, MaxMoves));
                    foreach (var move in responses.Moves)
                    {
                        string label = move.Command == BridgeNames.Pick
                            ? _text.Say("pick", ("id", Int(request.Id)), ("by", Str(_text.Entity(move.By!))),
                                ("choice", Str(_text.Label(request.Choices[(int)move.Arguments[BridgeNames.PickChoice].AsInt]))))
                            : Label(move.Command, move.Arguments);
                        options.Add(new Option(seat, label,
                            () => Text(Wait(session.Respond(request.Id, move.Command, move.Arguments)))));
                    }
                    foreach (var needs in responses.Needs)
                        _out.WriteLine(_text.Say("needs", ("command", Str(needs.Command)),
                            ("parameters", Str(string.Join(", ", needs.Parameters)))));
                }
                foreach (var command in Wait(session.Commands()))
                {
                    if (!command.Free) continue;
                    foreach (var move in Moves(command.Name, Wait(session.LegalMoves(command.Name, new Dictionary<string, BridgeValue>(), MaxMoves))))
                        options.Add(new Option(seat, Label(command.Name, move.Arguments),
                            () => Text(Wait(session.Submit(command.Name, move.Arguments)))));
                }
            }
            // 화면 동작은 모든 자리의 커맨드 뒤에: 게임을 바꾸는 수의 번호가 화면 동작 때문에 밀리지 않는다
            foreach (var seat in _seats) UiOptions(seat, Session(seat), options);
            return options;
        }

        /// <summary>
        /// 그 자리의 UI 동작 합법 수를 <c>[화면]</c>을 붙여 커맨드들 뒤에 둔다 (G285). <c>me</c> 매개변수가 있으면 앉은 엔티티마다 그것을 넣어
        /// 나열하고(G286), 없으면 자리마다 한 번. 고르면 <c>Act</c> 뒤 적힌 <c>submit</c>을 차례로 낸다(G283).
        /// </summary>
        private void UiOptions(string seat, IHostSession session, List<Option> options)
        {
            var seated = Wait(session.Seats());
            foreach (var action in Wait(session.UiActions()))
            {
                bool takesMe = action.Parameters.Any(p => p.Name == MeParameter);
                var givens = takesMe
                    ? seated.Select(me => (IReadOnlyDictionary<string, BridgeValue>)new Dictionary<string, BridgeValue> { [MeParameter] = me })
                    : new[] { (IReadOnlyDictionary<string, BridgeValue>)new Dictionary<string, BridgeValue>() };
                foreach (var given in givens)
                {
                    foreach (var move in Moves(action.Name, Wait(session.LegalUiMoves(action.Name, given, MaxMoves))))
                    {
                        var shown = move.Arguments.Where(p => !(takesMe && p.Key == MeParameter)).ToDictionary(p => p.Key, p => p.Value);
                        options.Add(new Option(seat, _text.Say("screen") + " " + Label(action.Name, shown),
                            () => Act(session, action.Name, move.Arguments)));
                    }
                }
            }
        }

        private string Act(IHostSession session, string action, IReadOnlyDictionary<string, BridgeValue> move)
        {
            var acted = Wait(session.Act(action, move));
            if (!acted.Succeeded) return acted.Outcome + Why(acted.Reasons);
            if (acted.Deliveries.Count == 0) return _text.Say("ok");
            var lines = new List<string>();
            foreach (var delivery in acted.Deliveries)
                lines.Add(Label(delivery.Command, delivery.Arguments) + ": " + Delivered(delivery.Result));
            return string.Join("; ", lines);
        }

        /// <summary>합법 수. 나열 중 오류가 나면 알리고 비운다. 호스트가 줘야 할 인자가 틀린 것은 말없이 비운다.</summary>
        private IReadOnlyList<BridgeMove> Moves(string command, BridgeMoves list)
        {
            foreach (var problem in list.Problems)
            {
                if (problem.Kind != BridgeReasonKind.Error) continue;
                _out.WriteLine(_text.Say("cannot-list", ("command", Str(command)), ("why", Str(problem.Message ?? ""))));
            }
            return list.Moves;
        }

        private string Label(string command, IReadOnlyDictionary<string, BridgeValue> move) =>
            command + "(" + string.Join(", ", move.Select(p => p.Key + "=" + _text.Of(p.Value))) + ")";

        // ---- 결과 ----

        private string Text(BridgeResult r) => r.Succeeded ? _text.Say("ok") : r.Outcome + Why(r.Reasons);

        /// <summary>화면 동작이 낸 <c>submit</c>의 결과. 낼 곳이 없어 거절되면 그 글 그대로.</summary>
        private string Delivered(BridgeResult r) =>
            r.Reasons.Count == 1 && r.Reasons[0].Kind == BridgeReasonKind.Refusal ? r.Reasons[0].Message! : Text(r);

        private string Why(IReadOnlyList<BridgeReason> reasons)
        {
            var texts = new List<string>();
            foreach (var reason in reasons)
            {
                switch (reason.Kind)
                {
                    case BridgeReasonKind.Require when reason.Text != null:
                        texts.Add(_text.Tr.Format(reason.Text));
                        break;
                    case BridgeReasonKind.Error:
                    case BridgeReasonKind.Argument:
                    case BridgeReasonKind.Refusal:
                        texts.Add(reason.Message ?? "");
                        break;
                    default:
                        texts.Add(reason.Kind + " " + (reason.Parameter ?? reason.Slot ?? ""));
                        break;
                }
            }
            return texts.Count == 0 ? "" : ": " + string.Join("; ", texts);
        }
    }
}
