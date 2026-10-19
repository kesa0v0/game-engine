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
        private readonly IReadOnlyList<string> _seats;
        private readonly TextReader _in;
        private readonly TextWriter _out;
        private readonly ValueText _text;

        public ConsoleGame(ContentLoadResult content, World world, IReadOnlyList<string> seats, TextReader input, TextWriter output)
        {
            _content = content;
            _world = world;
            _seats = seats;
            _in = input;
            _out = output;
            _text = new ValueText(world);
        }

        private sealed class Option
        {
            public Option(string label, Func<string, Outcome> run)
            {
                Label = label;
                Run = run;
            }

            public string Label { get; }

            /// <summary>연결(issuer) 하나로 낸다.</summary>
            public Func<string, Outcome> Run { get; }
        }

        /// <summary>제출·답·응답의 결과를 한 모양으로.</summary>
        private sealed class Outcome
        {
            public Outcome(bool succeeded, bool gateDenied, string message)
            {
                Succeeded = succeeded;
                GateDenied = gateDenied;
                Message = message;
            }

            public bool Succeeded { get; }
            public bool GateDenied { get; }
            public string Message { get; }
        }

        /// <summary>낼 수 있는 것이 없거나 입력이 끝나거나 <c>:q</c>면 끝난다.</summary>
        public void Run()
        {
            while (true)
            {
                WriteState();
                var options = Options();
                if (options.Count == 0)
                {
                    _out.WriteLine("낼 수 있는 것이 없다. 끝.");
                    return;
                }
                for (int i = 0; i < options.Count; i++)
                    _out.WriteLine("  " + (i + 1).ToString(CultureInfo.InvariantCulture) + ") " + options[i].Label);

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
                _out.WriteLine(Issue(picked));
            }
        }

        /// <summary>
        /// 구멍: 이 엔티티로 행동할 수 있는 연결이 누구인지 묻는 API가 없다(입구 정책은 모드 안에 있다).
        /// 그래서 자리마다 차례로 내 보고 입구가 막은 것만 넘어간다. 막힌 시도는 상태를 바꾸지 않는다(C8).
        /// </summary>
        private string Issue(Option option)
        {
            Outcome? last = null;
            foreach (var seat in _seats)
            {
                last = option.Run(seat);
                if (!last.GateDenied) return "[" + seat + "] " + last.Message;
            }
            return "[어느 자리도 낼 수 없다] " + last!.Message;
        }

        // ---- 상태 ----

        /// <summary>구멍: 무엇이 바뀌었는지 알려 주는 구독이 없어 매번 전부 다시 읽는다.</summary>
        private void WriteState()
        {
            _out.WriteLine();
            _out.WriteLine("== 틱 " + _world.Tick.ToString(CultureInfo.InvariantCulture) + " ==");
            foreach (var type in _content.Schema.Types)
            {
                foreach (var id in _world.All(type.Name))
                {
                    var parts = new List<string>();
                    foreach (var component in _content.Schema.Components)
                    {
                        if (!_world.Has(id, component.Name)) continue;
                        var members = component.Members.Select(m =>
                            m.Name + "=" + _text.Of(_world.Get(id, _world.Member(component.Name, m.Name))));
                        parts.Add(component.Name + "(" + string.Join(", ", members) + ")");
                    }
                    _out.WriteLine(_text.Entity(id) + "  " + string.Join(" ", parts));
                }
            }
            foreach (var request in _world.OpenRequests)
            {
                _out.WriteLine("질문 " + request.Id.ToString(CultureInfo.InvariantCulture) + ": " + _text.Of(request.Title) +
                               " → " + string.Join(", ", request.Who.Select(_text.Entity)) +
                               (request.Data is NoneValue ? "" : "  " + _text.Of(request.Data)));
            }
        }

        // ---- 할 수 있는 것 ----

        private List<Option> Options()
        {
            var options = new List<Option>();
            foreach (var request in _world.OpenRequests)
            {
                foreach (var by in request.Who)
                {
                    for (int i = 0; i < request.Choices.Count; i++)
                    {
                        long choice = i;
                        options.Add(new Option(
                            "질문 " + request.Id.ToString(CultureInfo.InvariantCulture) + " " + _text.Entity(by) + ": " +
                            _text.Of(request.Choices[i]),
                            seat => From(_world.Answer(seat, request.Id, by, choice))));
                    }
                    foreach (var command in request.Responses)
                    {
                        foreach (var move in Moves(command, ResponseArgs(command, by)))
                        {
                            options.Add(new Option(Label(command, move),
                                seat => From(_world.Respond(seat, request.Id, command, move))));
                        }
                    }
                }
            }
            foreach (var command in _content.Commands.Keys.OrderBy(k => k, StringComparer.Ordinal))
            {
                if (!_content.Commands[command].Free) continue;
                foreach (var move in Moves(command, new Dictionary<string, Value>()))
                    options.Add(new Option(Label(command, move), seat => From(_world.Submit(seat, command, move))));
            }
            return options;
        }

        /// <summary>합법 수. 호스트가 줘야 할 인자가 빠져 나열할 수 없으면 비운다.</summary>
        private IReadOnlyList<IReadOnlyDictionary<string, Value>> Moves(string command, IReadOnlyDictionary<string, Value> args)
        {
            try
            {
                return _world.LegalMoves(command, args, MaxMoves).Moves;
            }
            catch (WorldException e)
            {
                _out.WriteLine("(" + command + "의 수를 나열할 수 없다: " + e.Message + ")");
                return Array.Empty<IReadOnlyDictionary<string, Value>>();
            }
        }

        /// <summary>
        /// 구멍: 응답 커맨드의 합법 수를 질문 하나에 대해 묻는 API가 없다. 답하는 엔티티를 어느 매개변수에 넣을지는
        /// <c>acts_for</c> 람다 안에 있으므로, <c>lambda p: p</c> 모양이면 그 매개변수에 넣는다.
        /// </summary>
        private IReadOnlyDictionary<string, Value> ResponseArgs(string command, EntityId by)
        {
            var args = new Dictionary<string, Value>();
            var lambda = _content.Commands[command].ActsFor?.Lambda;
            if (lambda != null && lambda.Parameters.Count == 1 && lambda.Body is NameExpr name &&
                name.Name == lambda.Parameters[0].Name)
                args[name.Name] = new EntityValue(by);
            return args;
        }

        private string Label(string command, IReadOnlyDictionary<string, Value> move) =>
            command + "(" + string.Join(", ", move.Select(p => p.Key + "=" + _text.Of(p.Value))) + ")";

        // ---- 결과 ----

        private Outcome From(SubmitResult r) =>
            new Outcome(r.Succeeded, r.Outcome == SubmitOutcome.GateDenied,
                r.Succeeded ? "ok" : r.Outcome + Why(r.Error, r.Preconditions, r.ArgumentRejections));

        private Outcome From(AnswerResult r) =>
            new Outcome(r.Succeeded, r.Outcome == AnswerOutcome.GateDenied,
                r.Succeeded ? "ok" : r.Outcome + (r.Message == null ? "" : ": " + r.Message) +
                                     Why(r.Error, r.Preconditions, r.ArgumentRejections));

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
