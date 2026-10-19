using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Kesa.Engine.Loading;
using Kesa.Engine.Runtime;
using Kesa.Engine.Script;

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
        /// <summary>연결마다 앉힐 Def. 그 Def의 엔티티가 생기면 앉힌다.</summary>
        private readonly IReadOnlyList<(string Connection, string Def)> _wanted;

        /// <summary>연결 꼬리표, 처음 나온 순서.</summary>
        private readonly IReadOnlyList<string> _seats;

        private readonly TextReader _in;
        private readonly TextWriter _out;
        private readonly ValueText _text;

        public ConsoleGame(ContentLoadResult content, World world, IReadOnlyList<(string Connection, string Def)> seats,
            TextReader input, TextWriter output)
        {
            _content = content;
            _world = world;
            _wanted = seats;
            _seats = seats.Select(s => s.Connection).Distinct().ToList();
            _in = input;
            _out = output;
            _text = new ValueText(world);
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
                WriteState();
                var options = Options();
                if (options.Count == 0)
                {
                    _out.WriteLine("낼 수 있는 것이 없다. 끝.");
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
                        _out.WriteLine("1부터 " + options.Count.ToString(CultureInfo.InvariantCulture) + " 사이의 번호, 끝내려면 :q");
                }
                _out.WriteLine("[" + picked.Seat + "] " + picked.Run());
            }
        }

        // ---- 자리 ----

        /// <summary>
        /// 원하는 Def의 엔티티가 생겼고 아무도 앉지 않았으면 그 연결을 앉힌다 (G236의 호스트 쓰기 경로). 진짜 로비라면 모드가 고를 수 있는
        /// 엔티티 목록을 내놓아야 하는데 그 길은 아직 없다.
        /// </summary>
        private void SeatArrivals()
        {
            foreach (var (connection, def) in _wanted)
            {
                foreach (var type in _content.Schema.Types)
                {
                    foreach (var id in _world.All(type.Name))
                    {
                        if (_world.DefOf(id) != def || _world.SeatOf(id) != null) continue;
                        _world.Seat(id, connection);
                        _out.WriteLine("[" + connection + "] " + _text.Entity(id) + "에 앉음");
                        goto next;
                    }
                }
                next: ;
            }
        }

        // ---- 상태 ----

        /// <summary>엔티티마다 마지막으로 그린 줄과 그때의 도장. 도장이 그대로면 값을 다시 읽지 않는다 (G235).</summary>
        private readonly Dictionary<long, (long Version, string Line)> _lines = new Dictionary<long, (long, string)>();

        /// <summary>테스트용: 도장을 보지 않고 매번 다시 읽는다. 도장으로 건너뛴 화면과 견주는 데 쓴다.</summary>
        internal static bool AlwaysRedraw { get; set; }

        /// <summary>그 엔티티가 바뀌었을 때만(도장) 다시 읽는다. 다른 엔티티를 읽는 <c>derived</c> 값은 도장이 담지 않는다(G235의 남은 몫).</summary>
        private void WriteState()
        {
            _out.WriteLine();
            _out.WriteLine("== 틱 " + _world.Tick.ToString(CultureInfo.InvariantCulture) + " ==");
            foreach (var type in _content.Schema.Types)
            {
                foreach (var id in _world.All(type.Name))
                {
                    long version = _world.Version(id);
                    if (AlwaysRedraw || !_lines.TryGetValue(id.Value, out var drawn) || drawn.Version != version)
                        _lines[id.Value] = drawn = (version, Line(id));
                    _out.WriteLine(drawn.Line);
                }
            }
            foreach (var request in _world.OpenRequests)
            {
                _out.WriteLine("질문 " + request.Id.ToString(CultureInfo.InvariantCulture) + ": " + _text.Of(request.Title) +
                               " → " + string.Join(", ", request.Who.Select(_text.Entity)) +
                               (request.Data is NoneValue ? "" : "  " + _text.Of(request.Data)));
            }
        }

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
                    // 거른 질문의 대상은 이 자리가 답할 수 있는 것만이다 (G234)
                    foreach (var by in request.Who)
                    {
                        for (int i = 0; i < request.Choices.Count; i++)
                        {
                            long choice = i;
                            options.Add(new Option(seat,
                                "질문 " + request.Id.ToString(CultureInfo.InvariantCulture) + " " + _text.Entity(by) + ": " +
                                _text.Of(request.Choices[i]),
                                () => Text(_world.Answer(seat, request.Id, by, choice))));
                        }
                    }
                    var responses = view.LegalResponses(request.Id, null, MaxMoves);
                    foreach (var move in responses.Moves)
                    {
                        options.Add(new Option(seat, Label(move.Command, move.Arguments),
                            () => Text(_world.Respond(seat, request.Id, move.Command, move.Arguments))));
                    }
                    foreach (var needs in responses.Needs)
                        _out.WriteLine("(" + needs.Command + "의 수를 나열할 수 없다: " + string.Join(", ", needs.Parameters) + "를 정해야 한다)");
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
                _out.WriteLine("(" + command + "의 수를 나열할 수 없다: " + e.Message + ")");
                return Array.Empty<IReadOnlyDictionary<string, Value>>();
            }
        }

        private string Label(string command, IReadOnlyDictionary<string, Value> move) =>
            command + "(" + string.Join(", ", move.Select(p => p.Key + "=" + _text.Of(p.Value))) + ")";

        // ---- 결과 ----

        private string Text(SubmitResult r) =>
            r.Succeeded ? "ok" : r.Outcome + Why(r.Error, r.Preconditions, r.ArgumentRejections);

        private string Text(AnswerResult r) =>
            r.Succeeded ? "ok" : r.Outcome + (r.Message == null ? "" : ": " + r.Message) +
                                 Why(r.Error, r.Preconditions, r.ArgumentRejections);

        private string Why(WorldException? error, PreconditionReport? report, IReadOnlyList<ArgumentRejection> rejections)
        {
            var reasons = new List<string>();
            if (error != null) reasons.Add(error.Message);
            foreach (var rejection in rejections) reasons.Add(rejection.Message);
            if (report != null)
            {
                foreach (var failure in report.Failures)
                    reasons.Add(failure.MessageKey ?? failure.Kind + " " + (failure.Parameter ?? failure.Slot ?? ""));
            }
            return reasons.Count == 0 ? "" : ": " + string.Join("; ", reasons);
        }
    }
}
