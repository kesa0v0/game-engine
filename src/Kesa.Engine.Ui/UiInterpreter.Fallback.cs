using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Kesa.Engine.Bridge;

namespace Kesa.Engine.Ui
{
    public sealed partial class UiInterpreter
    {
        // ---- 아무도 그리지 않은 질문의 기본 창 (G386) ----

        /// <summary>엔진 기본 창으로 보이는 질문 번호 → 창 요소.</summary>
        private readonly SortedDictionary<long, ElementId> _fallback = new SortedDictionary<long, ElementId>();

        /// <summary>한 세션의 해석기가 여럿이어도 기본 창은 첫 해석기만 그린다(같은 질문이 두 번 뜨지 않게).</summary>
        private bool DrawsFallback => _keyPrefix == "p";

        /// <summary>합법 응답을 이만큼까지만 버튼으로 만든다.</summary>
        private const int FallbackAnswers = 20;

        /// <summary>
        /// 화면에 붙은 어느 바인딩도 <c>questions()</c>로 읽지 않은 열린 질문을 기본 창으로 보이고, 닫혔거나 모드가 읽기 시작한 질문의 창은 없앤다.
        /// 기본 창이 처음 뜨는 질문 종류는 <see cref="Errors"/>에 개발 경고를 남긴다.
        /// </summary>
        private async ValueTask SyncFallbackAsync()
        {
            if (!DrawsFallback) return;
            var undrawn = await _session.UndrawnRequests();
            var clicked = ClickedCommands();
            var open = new SortedSet<long>();
            foreach (var q in undrawn)
            {
                if (AnsweredByButton(q, clicked)) continue;
                open.Add(q.Id);
                if (_fallback.ContainsKey(q.Id)) continue;
                Report("question '" + q.Name + "'", "no screen function reads questions(\"" + q.Name + "\"), so the engine shows it in its " +
                                                    "own window; draw it in the mod's UI (spec-gaps G386)");
                _fallback[q.Id] = await BuildFallbackAsync(q);
            }
            var gone = new List<long>();
            foreach (var pair in _fallback)
                if (!open.Contains(pair.Key)) gone.Add(pair.Key);
            foreach (long id in gone)
            {
                Windows.Remove(_renderer, _fallback[id]);
                _renderer.Remove(_fallback[id]);
                _fallback.Remove(id);
            }
        }

        /// <summary>지금 화면에 붙은 <c>on-click</c>이 가리키는 커맨드(화면 동작은 뺀다).</summary>
        private SortedSet<string> ClickedCommands()
        {
            var names = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var node in _eventNodes)
            {
                if (!node.Alive) continue;
                foreach (var e in node.Definition.Events)
                    if (e.Name == "click" && _commands.ContainsKey(e.Call.Function)) names.Add(e.Call.Function);
            }
            return names;
        }

        /// <summary>그 질문의 응답 커맨드를 내는 버튼이 화면에 있으면 모드가 그린 것으로 친다.</summary>
        private static bool AnsweredByButton(BridgeRequest q, SortedSet<string> clicked)
        {
            foreach (var response in q.Responses)
                if (clicked.Contains(response)) return true;
            return false;
        }

        private async ValueTask<ElementId> BuildFallbackAsync(BridgeRequest q)
        {
            var window = _renderer.Create("VisualElement", null, 0, new[]
            {
                new KeyValuePair<string, string>("name", "kesa-question-" + q.Id),
                new KeyValuePair<string, string>("class", "kesa-question window"),
            });
            Windows.Add(_renderer, window, new UiWindow(UiWindowKind.Normal, closeOnEscape: false, closeOnOutside: false, raise: true), null);
            Windows.SetVisible(_renderer, window, true);
            var title = _renderer.Create("Label", window, 0, new[] { new KeyValuePair<string, string>("class", "kesa-question-title") });
            _renderer.SetText(title, "text", _text(q.Title));

            var moves = await _session.LegalResponses(q.Id, FallbackAnswers);
            int at = 1;
            foreach (var move in moves.Moves)
            {
                var button = _renderer.Create("Button", window, at++, new[] { new KeyValuePair<string, string>("class", "kesa-question-answer") });
                _renderer.SetText(button, "text", AnswerText(q, move));
                var captured = move;
                _renderer.OnClick(button, () => AnswerFallbackAsync(q.Id, captured));
            }
            return window;
        }

        /// <summary>선택지 답은 선택지의 보일 이름(없으면 값), 응답 커맨드는 커맨드와 인자. 답할 엔티티가 여럿이면 그것도 붙인다.</summary>
        private string AnswerText(BridgeRequest q, BridgeMove move)
        {
            string text;
            if (move.Command == BridgeNames.Pick && move.Arguments.TryGetValue(BridgeNames.PickChoice, out var picked) &&
                picked is BridgeInt index && index.Value >= 0 && index.Value < q.Choices.Count)
            {
                var choice = q.Choices[(int)index.Value];
                text = _text(choice.Label ?? choice.Value);
            }
            else
            {
                var parts = new List<string>();
                foreach (var pair in move.Arguments) parts.Add(pair.Key + "=" + _text(pair.Value));
                text = move.Command + (parts.Count == 0 ? "" : " (" + string.Join(", ", parts) + ")");
            }
            return q.Who.Count > 1 && move.By != null ? text + " — " + _text(move.By) : text;
        }

        private async ValueTask AnswerFallbackAsync(long request, BridgeMove move)
        {
            var result = await _session.Respond(request, move.Command, move.Arguments);
            ActionResult?.Invoke(move.Command, result);
            _recheck = true;
            await RefreshAsync();
        }
    }
}
