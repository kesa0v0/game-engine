using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Kesa.Engine.Loading;
using Kesa.Engine.Runtime;
using Kesa.Engine.Schema;
using Kesa.Engine.Script;
using Kesa.Engine.Text;

namespace Kesa.Engine.ConsoleHost
{
    /// <summary>
    /// 터미널 핫시트 한 판. 게임 지식 없이 커널 공개 API만으로 화면을 만든다: 상태는 스키마를 따라 엔티티를 늘어놓고,
    /// 할 수 있는 것은 열린 질문(선택지·응답 커맨드의 합법 수)과 자유 커맨드의 합법 수다.
    /// 여기서 우회한 것은 커널 API의 구멍 후보다(주석의 "구멍").
    /// </summary>
    public sealed class ConsoleGame
    {
        /// <summary>커맨드 하나에서 보여 줄 합법 수의 상한.</summary>
        public const int MaxMoves = 50;

        private readonly ContentLoadResult _content;
        private readonly World _world;
        /// <summary>연결마다 앉힐 Def. 그 Def의 엔티티가 생기면 앉힌다. Def가 null이면 후보가 생길 때 고르게 묻는다.</summary>
        private readonly IReadOnlyList<(string Connection, string? Def)> _wanted;

        /// <summary>이미 자리를 물은 연결. 한 번만 묻는다(앉은 진영이 죽어도 다시 묻지 않는다).</summary>
        private readonly HashSet<string> _asked = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>연결 꼬리표, 처음 나온 순서.</summary>
        private readonly IReadOnlyList<string> _seats;

        private readonly TextReader _in;
        private readonly TextWriter _out;
        private readonly ValueText _text;

        /// <summary>모드의 <c>ui/console.txt</c> (G250). 비면 엔티티를 늘어놓는다.</summary>
        private readonly IReadOnlyList<ConsoleUi.Panel> _panels;

        public ConsoleGame(ContentLoadResult content, World world, IReadOnlyList<(string Connection, string? Def)> seats,
            IReadOnlyList<ConsoleUi.Panel> panels, Translator translator, TextReader input, TextWriter output)
        {
            _content = content;
            _world = world;
            _wanted = seats;
            _panels = panels;
            _seats = seats.Select(s => s.Connection).Distinct().ToList();
            _in = input;
            _out = output;
            _text = new ValueText(world, translator);
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
                        _out.WriteLine(_text.Say("pick-number", ("n", new IntValue(options.Count))));
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
                foreach (var type in _content.Schema.Types)
                {
                    foreach (var id in _world.All(type.Name))
                    {
                        if (_world.DefOf(id) != def || _world.SeatOf(id) != null) continue;
                        _world.Seat(id, connection);
                        _out.WriteLine("[" + connection + "] " + Seated(id));
                        goto next;
                    }
                }
                next: ;
            }
        }

        /// <summary>
        /// 작은 로비: Def 없이 적은 연결마다, 빈 앉을 후보(<c>World.SeatChoices</c>, G239)가 생기면 그중 하나를 고르게 묻는다. 입력이
        /// 끝나거나 <c>:q</c>면 false.
        /// </summary>
        private bool AskSeats()
        {
            foreach (var (connection, def) in _wanted)
            {
                if (def != null || _asked.Contains(connection) || _world.SeatsOf(connection).Count > 0) continue;
                var free = _world.SeatChoices().Where(id => _world.SeatOf(id) == null).ToList();
                if (free.Count == 0) continue; // 후보가 아직 없다: 생기면 묻는다
                _asked.Add(connection);
                _out.WriteLine(_text.Say("seat-choices") + " " + string.Join("  ",
                    free.Select((id, i) => (i + 1).ToString(CultureInfo.InvariantCulture) + ") " + _text.Entity(id))));
                while (true)
                {
                    _out.Write(_text.Say("where", ("connection", new StrValue(connection))) + " ");
                    string? line = _in.ReadLine();
                    if (line == null || line.Trim() == ":q") return false;
                    if (int.TryParse(line.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) &&
                        n >= 1 && n <= free.Count)
                    {
                        _world.Seat(free[n - 1], connection);
                        _out.WriteLine("[" + connection + "] " + Seated(free[n - 1]));
                        break;
                    }
                    _out.WriteLine(_text.Say("pick-number", ("n", new IntValue(free.Count))));
                }
            }
            return true;
        }

        private string Seated(EntityId id) => _text.Say("seated", ("entity", new StrValue(_text.Entity(id))));

        // ---- 상태 ----

        /// <summary>엔티티마다 마지막으로 그린 줄과 그때의 도장. 도장이 그대로면 값을 다시 읽지 않는다 (G235).</summary>
        private readonly Dictionary<long, (long Version, string Line)> _lines = new Dictionary<long, (long, string)>();

        /// <summary>테스트용: 도장을 보지 않고 매번 다시 읽는다. 도장으로 건너뛴 화면과 견주는 데 쓴다.</summary>
        internal static bool AlwaysRedraw { get; set; }

        /// <summary>모드의 UI 파일이 있으면 그 패널들, 없으면 엔티티를 늘어놓고, 열린 질문을 덧붙인다.</summary>
        private void WriteState()
        {
            _out.WriteLine();
            _out.WriteLine(_text.Say("tick", ("tick", new IntValue(_world.Tick))));
            if (_panels.Count > 0) WritePanels();
            else WriteEntities();
            foreach (var request in _world.OpenRequests)
            {
                _out.WriteLine(_text.Say("question", ("id", new IntValue(request.Id)), ("title", new StrValue(_text.Label(request.Title))),
                                   ("who", new StrValue(string.Join(", ", request.Who.Select(_text.Entity))))) +
                               (request.Data is NoneValue ? "" : "  " + _text.Of(request.Data)));
            }
        }

        /// <summary>
        /// 자리마다, 그 연결이 앉은 엔티티마다 UI 파일의 함수를 그 연결이 보는 세계에서 부른다 (G248·G250). 지난 결과의 영수증이
        /// 그대로면 부르지 않는다(G246). 부르다 실패한 칸은 오류 줄을 보이고 나머지는 계속 그린다(G247).
        /// </summary>
        private void WritePanels()
        {
            foreach (var seat in _seats)
            {
                foreach (var me in _world.SeatsOf(seat))
                {
                    _out.WriteLine("[" + seat + "] " + _text.Entity(me));
                    var view = _world.ViewAs(seat);
                    foreach (var panel in _panels)
                    {
                        var key = (seat, me.Value, panel.Place);
                        if (AlwaysRedraw || !_drawn.TryGetValue(key, out var drawn) || drawn.Result == null || drawn.Result.Changed(_world))
                            _drawn[key] = drawn = Draw(view, panel, me);
                        foreach (var line in drawn.Lines) _out.WriteLine(line);
                    }
                }
            }
        }

        /// <summary>(연결, 앉은 엔티티, 패널 곳)마다 지난 결과와 그린 줄. 실패한 칸은 결과가 없어 다음에 다시 부른다.</summary>
        private readonly Dictionary<(string, long, string), (CallResult? Result, List<string> Lines)> _drawn =
            new Dictionary<(string, long, string), (CallResult?, List<string>)>();

        private (CallResult? Result, List<string> Lines) Draw(WorldView view, ConsoleUi.Panel panel, EntityId me)
        {
            CallResult result;
            try
            {
                result = view.Call(panel.Function, new EntityValue(me));
            }
            catch (WorldException e)
            {
                return (null, new List<string> { "  " + panel.Place + ": " + e.Message });
            }
            var lines = new List<string>();
            if (result.Value is ListValue list)
                foreach (var item in list.Items) lines.Add("  " + _text.Of(item));
            else lines.Add("  " + _text.Of(result.Value));
            return (result, lines);
        }

        /// <summary>
        /// 그 엔티티가 바뀌었을 때만(도장) 다시 읽는다. 도장은 저장된 값의 것이라 남의 엔티티를 읽는 <c>derived</c>를 담지 않으므로,
        /// <c>derived</c> 칸이 있는 엔티티는 매번 다시 읽는다 (G251).
        /// </summary>
        private void WriteEntities()
        {
            foreach (var type in _content.Schema.Types)
            {
                foreach (var id in _world.All(type.Name))
                {
                    long version = _world.Version(id);
                    if (AlwaysRedraw || HasDerived(id) || !_lines.TryGetValue(id.Value, out var drawn) || drawn.Version != version)
                        _lines[id.Value] = drawn = (version, Line(id));
                    _out.WriteLine(drawn.Line);
                }
            }
        }

        private bool HasDerived(EntityId id) =>
            _content.Schema.Components.Any(c => c.Members.Any(m => m.Kind == MemberKind.Derived) && _world.Has(id, c.Name));

        private string Line(EntityId id)
        {
            var parts = new List<string>();
            foreach (var component in _content.Schema.Components)
            {
                if (!_world.Has(id, component.Name)) continue;
                var members = component.Members.Select(m =>
                    m.Name + "=" + _text.Of(_world.Get(id, _world.Member(component.Name, m.Name))));
                parts.Add(component.Name + "(" + string.Join(", ", members) + ")");
            }
            return _text.Entity(id) + "  " + string.Join(" ", parts);
        }

        // ---- 할 수 있는 것 ----

        /// <summary>자리마다 그 연결로서 보는 읽기(<see cref="World.ViewAs"/>)로 그 자리가 낼 수 있는 것만 모은다 (G200·G232·G233).</summary>
        private List<Option> Options()
        {
            var options = new List<Option>();
            foreach (var seat in _seats)
            {
                var view = _world.ViewAs(seat);
                foreach (var request in view.OpenRequests)
                {
                    // 선택지 답(engine.pick)과 응답 커맨드가 한 목록이고 어느 것이든 Respond로 낸다 (G272).
                    // 거른 질문의 대상은 이 자리가 답할 수 있는 것만이다 (G234)
                    var responses = view.LegalResponses(request.Id, null, MaxMoves);
                    foreach (var move in responses.Moves)
                    {
                        string label = move.Command == World.PickCommand
                            ? _text.Say("pick", ("id", new IntValue(request.Id)), ("by", new StrValue(_text.Entity(move.By))),
                                ("choice", new StrValue(_text.Label(request.Choices[(int)((IntValue)move.Arguments[World.PickChoice]).Value]))))
                            : Label(move.Command, move.Arguments);
                        options.Add(new Option(seat, label,
                            () => Text(_world.Respond(seat, request.Id, move.Command, move.Arguments))));
                    }
                    foreach (var needs in responses.Needs)
                        _out.WriteLine(_text.Say("needs", ("command", new StrValue(needs.Command)),
                            ("parameters", new StrValue(string.Join(", ", needs.Parameters)))));
                }
                foreach (var command in _content.Commands.Keys.OrderBy(k => k, StringComparer.Ordinal))
                {
                    if (!_content.Commands[command].Free) continue;
                    foreach (var move in Moves(view, command, new Dictionary<string, Value>()))
                        options.Add(new Option(seat, Label(command, move), () => Text(_world.Submit(seat, command, move))));
                }
            }
            return options;
        }

        /// <summary>합법 수. 호스트가 줘야 할 인자가 빠져 나열할 수 없으면 비운다.</summary>
        private IReadOnlyList<IReadOnlyDictionary<string, Value>> Moves(WorldView view, string command, IReadOnlyDictionary<string, Value> args)
        {
            try
            {
                return view.LegalMoves(command, args, MaxMoves).Moves;
            }
            catch (WorldException e)
            {
                _out.WriteLine(_text.Say("cannot-list", ("command", new StrValue(command)), ("why", new StrValue(e.Message))));
                return Array.Empty<IReadOnlyDictionary<string, Value>>();
            }
        }

        private string Label(string command, IReadOnlyDictionary<string, Value> move) =>
            command + "(" + string.Join(", ", move.Select(p => p.Key + "=" + _text.Of(p.Value))) + ")";

        // ---- 결과 ----

        private string Text(SubmitResult r) =>
            r.Succeeded ? _text.Say("ok") : r.Outcome + Why(r.Error, r.Preconditions, r.ArgumentRejections);

        private string Text(AnswerResult r) =>
            r.Succeeded ? _text.Say("ok") : r.Outcome + (r.Message == null ? "" : ": " + r.Message) +
                                 Why(r.Error, r.Preconditions, r.ArgumentRejections);

        private string Why(WorldException? error, PreconditionReport? report, IReadOnlyList<ArgumentRejection> rejections)
        {
            var reasons = new List<string>();
            if (error != null) reasons.Add(error.Message);
            foreach (var rejection in rejections) reasons.Add(rejection.Message);
            if (report != null)
            {
                foreach (var failure in report.Failures)
                    reasons.Add(failure.MessageKey != null
                        ? _text.Message(failure.MessageKey, failure.Args)
                        : failure.Kind + " " + (failure.Parameter ?? failure.Slot ?? ""));
            }
            return reasons.Count == 0 ? "" : ": " + string.Join("; ", reasons);
        }
    }
}
