using System;
using System.Collections.Generic;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Script;

namespace Kesa.Engine.Runtime
{
    /// <summary>
    /// 스크립트가 이 연결이 답할 열린 질문을 읽는다 (G381·G383): <c>questions()</c>와 <c>questions("이름")</c>. "내게"는 보는 쪽(<see cref="_viewer"/>)이라
    /// 화면 함수·<c>@ui_action</c>·<c>@bot</c>에서만 돈다. 보이는 범위는 <see cref="WorldView.OpenRequests"/>와 같다(G200·G234).
    /// 읽은 질문 하나에 답하는 길은 <c>respond(q, 커맨드, …)</c>다(G384).
    /// </summary>
    public sealed partial class World
    {
        /// <summary>
        /// 호스트가 부른 함수가 <c>questions()</c>로 읽은 질문 이름(이름 없이 읽었으면 빈 문자열). 영수증이 열린 질문의 바뀜을 보고,
        /// 호스트는 아무도 읽지 않은 질문을 기본 창으로 보인다(G386). 읽지 않았으면 null.
        /// </summary>
        private SortedSet<string>? _questionsRead;

        private Value QuestionsBuiltin(IReadOnlyList<Value> positional, IReadOnlyDictionary<string, Value> keywords)
        {
            if (keywords.Count > 0 || positional.Count > 1 || positional.Count == 1 && !(positional[0] is StrValue))
            {
                throw new ScriptRuntimeException(Codes.RunType, "questions() takes nothing or the name of a kind of question (str)",
                    "write questions() for every open question you can answer, or questions(\"skirmish-hit\") (spec-gaps G383)");
            }
            // 시험 판에서는 봇이 모든 자리에 앉아 있다: 열린 질문 모두 (G459)
            if (IsTrial)
            {
                var all = new List<Value>();
                foreach (var q in OpenRequests)
                    if (positional.Count == 0 || q.Name == ((StrValue)positional[0]).Value) all.Add(new QuestionValue(this, q));
                return new ListValue(all);
            }
            if (_viewer == null)
            {
                throw new ScriptRuntimeException(Codes.RunNotWritable,
                    "questions() can only be used on a screen, in a @ui_action or in a @bot",
                    FunctionEffects.QuestionsHint);
            }
            string? name = positional.Count == 1 ? ((StrValue)positional[0]).Value : null;
            if (_whenReads != null)
            {
                (_questionsRead ??= new SortedSet<string>(StringComparer.Ordinal)).Add(name ?? "");
                foreach (var q in OpenRequests)
                foreach (var who in q.Who)
                    Read(who.Value, AliveRead);
            }
            var result = new List<Value>();
            foreach (var q in _viewer.OpenRequests)
                if (name == null || q.Name == name) result.Add(new QuestionValue(this, q));
            return new ListValue(result);
        }

        /// <summary>
        /// <c>respond(q, "커맨드", 이름=…)</c> (G384): <c>submit</c>처럼 적어 두고 동작이 끝나면 호스트가 그 질문에 낸다. 커맨드는 그 질문의 응답
        /// 커맨드이거나 선택지 질문의 <c>engine.pick</c>이다.
        /// </summary>
        private Value RespondBuiltin(string mod, IReadOnlyList<Value> positional, IReadOnlyDictionary<string, Value> keywords)
        {
            var submissions = _act?.Submissions ?? _botRun?.Submissions ?? throw OutsideUi("respond()");
            NoSubmitWhileImagining("respond()");
            submissions.Add(ReadRespond("respond()", mod, positional, keywords));
            return NoneValue.Instance;
        }

        /// <summary><c>respond(q, "커맨드", 이름=…)</c>의 인자를 읽는다 (<c>play</c>도 같은 꼴, G458).</summary>
        private UiSubmission ReadRespond(string what, string mod, IReadOnlyList<Value> positional, IReadOnlyDictionary<string, Value> keywords)
        {
            if (positional.Count != 2 || !(positional[0] is QuestionValue q) || !(positional[1] is StrValue name))
            {
                throw new ScriptRuntimeException(Codes.RunType,
                    what + " takes a question from questions(), the command name and then its arguments by name",
                    "write respond(q, \"engine.pick\", choice=0) or respond(q, \"Move\", to=cell) (spec-gaps G384)");
            }
            string id = name.Value.IndexOf('.') >= 0 ? name.Value : mod + "." + name.Value;
            if (!_commands.ContainsKey(id) && id != PickCommand)
            {
                throw new ScriptRuntimeException(Codes.RunUnknownName, what + " names '" + name.Value + "', but there is no command '" + id + "'",
                    Suggest.Hint(id, _commands.Keys));
            }
            var args = new SortedDictionary<string, Value>(StringComparer.Ordinal);
            foreach (var pair in keywords) args[pair.Key] = ToPlain("argument '" + pair.Key + "' of " + what, pair.Value);
            return new UiSubmission(id, args, q.Request.Id);
        }

        /// <summary><c>respond</c>가 적은 답을 그 질문에 낸다 (G384). 질문이 이미 닫혔거나 그 커맨드를 받지 않으면 이유를 붙여 거절한다.</summary>
        private DeliveryResult DeliverTo(string issuer, long request, UiSubmission submission)
        {
            Request? q = null;
            foreach (var open in OpenRequests)
                if (open.Id == request) q = open;
            if (q == null) return new DeliveryResult(null, null, "request " + request + " is no longer open");
            if (submission.Command == PickCommand) return DeliverPick(issuer, submission.Arguments, q);
            if (!_commands.ContainsKey(submission.Command))
            {
                throw new WorldException(Codes.RunUnknownName, "there is no command '" + submission.Command + "'",
                    Suggest.Hint(submission.Command, _commands.Keys));
            }
            if (IndexOf(q.Responses, submission.Command) < 0)
                return new DeliveryResult(null, null, "request " + request + " does not accept " + submission.Command);
            return new DeliveryResult(null, Respond(issuer, request, submission.Command, submission.Arguments), null);
        }

        /// <summary>
        /// <c>questions()</c>가 주는 질문 하나 (G383): 이 연결이 보는 모양으로 거른 질문이다. 저장할 수 없다: 질문은 닫히면 사라진다.
        /// </summary>
        private sealed class QuestionValue : Value, IRuntimeAttributes, IValueEquality
        {
            private static readonly string[] Attributes = { "name", "title", "data", "choices", "responses", "who" };

            private readonly World _world;

            public QuestionValue(World world, Request request)
            {
                _world = world;
                Request = request;
            }

            public Request Request { get; }

            public override string KindName => "question";

            public bool ValueEquals(Value other) => other is QuestionValue q && q.Request.Id == Request.Id;

            /// <summary>시험 판에서 이 봇이 원래 못 보던 질문이면 내용을 가린다 (G466).</summary>
            private bool Masked => _world.Current.QuestionMasked(Request.Id);

            public Value GetAttribute(string name)
            {
                switch (name)
                {
                    case "name": return new StrValue(Request.Name);
                    case "title": return Masked ? HiddenValue.Instance : _world.Current.UiRead(Request.Title);
                    case "data": return Masked ? HiddenValue.Instance : _world.Current.UiRead(Request.Data);
                    case "choices":
                    {
                        var choices = new List<Value>(Request.Choices.Count);
                        for (int i = 0; i < Request.Choices.Count; i++) choices.Add(new ChoiceView(_world, Request.Choices[i], i, Masked));
                        return new ListValue(choices);
                    }
                    case "responses":
                    {
                        var responses = new List<Value>(Request.Responses.Count);
                        foreach (var r in Request.Responses) responses.Add(new StrValue(r));
                        return new ListValue(responses);
                    }
                    case "who":
                    {
                        var who = new List<Value>(Request.Who.Count);
                        foreach (var e in Request.Who) who.Add(_world.Current.ToScript(new EntityValue(e)));
                        return new ListValue(who);
                    }
                }
                throw new ScriptRuntimeException(Codes.RunScript, "a question has no attribute '" + name + "'",
                    Suggest.Hint(name, Attributes) ?? "it has name, title, data, choices, responses and who");
            }
        }

        /// <summary>질문 값의 선택지 하나 (G271·G383·G448): 고를 값과 보일 글·데이터, 그리고 <c>engine.pick</c>에 넘길 번호.</summary>
        private sealed class ChoiceView : Value, IRuntimeAttributes, IValueEquality
        {
            private static readonly string[] Attributes = { "value", "label", "desc", "data", "index" };

            private readonly World _world;
            private readonly OptionValue _option;
            private readonly int _index;

            /// <summary>번호만 읽히고 내용은 가려짐이다 (G466).</summary>
            private readonly bool _masked;

            public ChoiceView(World world, OptionValue option, int index, bool masked = false)
            {
                _world = world;
                _option = option;
                _index = index;
                _masked = masked;
            }

            public override string KindName => "choice";

            public bool ValueEquals(Value other) => other is ChoiceView c && c._index == _index && PureOps.ValuesEqual(c._option, _option);

            public Value GetAttribute(string name)
            {
                if (_masked && name != "index" && Array.IndexOf(Attributes, name) >= 0) return HiddenValue.Instance;
                switch (name)
                {
                    case "value": return _world.Current.UiRead(_option.Value);
                    case "label": return _option.Label == null ? NoneValue.Instance : _world.Current.UiRead(_option.Label);
                    case "desc": return _option.Desc == null ? NoneValue.Instance : _world.Current.UiRead(_option.Desc);
                    case "data": return _option.Data == null ? NoneValue.Instance : _world.Current.UiRead(_option.Data);
                    case "index": return new IntValue(_index);
                }
                throw new ScriptRuntimeException(Codes.RunScript, "a choice has no attribute '" + name + "'",
                    Suggest.Hint(name, Attributes) ?? "it has value, label, desc, data and index");
            }
        }
    }
}
