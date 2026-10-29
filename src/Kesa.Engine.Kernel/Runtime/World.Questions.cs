using System.Collections.Generic;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Script;

namespace Kesa.Engine.Runtime
{
    /// <summary>
    /// 스크립트가 이 연결이 답할 열린 질문을 읽는다 (G381·G383): <c>questions()</c>와 <c>questions("이름")</c>. "내게"는 보는 쪽(<see cref="_viewer"/>)이라
    /// 화면 함수·<c>@ui_action</c>·<c>@bot</c>에서만 돈다. 보이는 범위는 <see cref="WorldView.OpenRequests"/>와 같다(G200·G234).
    /// </summary>
    public sealed partial class World
    {
        /// <summary>호스트가 부른 함수가 <c>questions()</c>를 읽었다: 영수증이 열린 질문의 바뀜을 본다.</summary>
        private bool _readsQuestions;

        private Value QuestionsBuiltin(IReadOnlyList<Value> positional, IReadOnlyDictionary<string, Value> keywords)
        {
            if (keywords.Count > 0 || positional.Count > 1 || positional.Count == 1 && !(positional[0] is StrValue))
            {
                throw new ScriptRuntimeException(Codes.RunType, "questions() takes nothing or the name of a kind of question (str)",
                    "write questions() for every open question you can answer, or questions(\"skirmish-hit\") (spec-gaps G383)");
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
                _readsQuestions = true;
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
        /// <c>questions()</c>가 주는 질문 하나 (G383): 이 연결이 보는 모양으로 거른 질문이다. 저장할 수 없다: 질문은 닫히면 사라진다.
        /// </summary>
        private sealed class QuestionValue : Value, IRuntimeAttributes, IValueEquality
        {
            private static readonly string[] Attributes = { "name", "title", "hint", "data", "choices", "responses", "who" };

            private readonly World _world;

            public QuestionValue(World world, Request request)
            {
                _world = world;
                Request = request;
            }

            public Request Request { get; }

            public override string KindName => "question";

            public bool ValueEquals(Value other) => other is QuestionValue q && q.Request.Id == Request.Id;

            public Value GetAttribute(string name)
            {
                switch (name)
                {
                    case "name": return new StrValue(Request.Name);
                    case "title": return _world.UiRead(Request.Title);
                    case "hint": return Request.Hint == null ? NoneValue.Instance : new StrValue(Request.Hint);
                    case "data": return _world.UiRead(Request.Data);
                    case "choices":
                    {
                        var choices = new List<Value>(Request.Choices.Count);
                        for (int i = 0; i < Request.Choices.Count; i++) choices.Add(new ChoiceView(_world, Request.Choices[i], i));
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
                        foreach (var e in Request.Who) who.Add(_world.ToScript(new EntityValue(e)));
                        return new ListValue(who);
                    }
                }
                throw new ScriptRuntimeException(Codes.RunScript, "a question has no attribute '" + name + "'",
                    Suggest.Hint(name, Attributes) ?? "it has name, title, hint, data, choices, responses and who");
            }
        }

        /// <summary>질문 값의 선택지 하나 (G271·G383): 고를 값과 보이는 모양, 그리고 <c>engine.pick</c>에 넘길 번호.</summary>
        private sealed class ChoiceView : Value, IRuntimeAttributes, IValueEquality
        {
            private static readonly string[] Attributes = { "value", "label", "desc", "style", "data", "index" };

            private readonly World _world;
            private readonly OptionValue _option;
            private readonly int _index;

            public ChoiceView(World world, OptionValue option, int index)
            {
                _world = world;
                _option = option;
                _index = index;
            }

            public override string KindName => "choice";

            public bool ValueEquals(Value other) => other is ChoiceView c && c._index == _index && PureOps.ValuesEqual(c._option, _option);

            public Value GetAttribute(string name)
            {
                switch (name)
                {
                    case "value": return _world.UiRead(_option.Value);
                    case "label": return _option.Label == null ? NoneValue.Instance : _world.UiRead(_option.Label);
                    case "desc": return _option.Desc == null ? NoneValue.Instance : _world.UiRead(_option.Desc);
                    case "style": return _option.Style == null ? NoneValue.Instance : new StrValue(_option.Style);
                    case "data": return _option.Data == null ? NoneValue.Instance : _world.UiRead(_option.Data);
                    case "index": return new IntValue(_index);
                }
                throw new ScriptRuntimeException(Codes.RunScript, "a choice has no attribute '" + name + "'",
                    Suggest.Hint(name, Attributes) ?? "it has value, label, desc, style, data and index");
            }
        }
    }
}
