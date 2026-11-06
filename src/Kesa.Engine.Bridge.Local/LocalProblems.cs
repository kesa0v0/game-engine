using System;
using System.Collections.Generic;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Runtime;

namespace Kesa.Engine.Bridge.Local
{
    /// <summary>판 주인이 읽는 문제 기록 (G470). 결과를 연결에게 거르기 전의 것을 받는다.</summary>
    internal sealed class LocalProblems
    {
        /// <summary>매 틱 실패하는 핸들러가 있어도 메모리가 늘지 않게.</summary>
        internal const int Kept = 1000;

        private readonly Queue<BridgeProblem> _items = new Queue<BridgeProblem>();
        private long _stamp;
        private long _droppedThrough;

        public void Note(long tick, IReadOnlyList<Reaction> reactions)
        {
            foreach (var reaction in reactions)
            {
                if (reaction.Outcome == ReactionOutcome.ScriptError && reaction.Error != null) Note(tick, reaction.Handler, reaction.Error);
                else if (reaction.Outcome == ReactionOutcome.Dropped) Add(tick, false, null, reaction.Handler, reaction.Message ?? "the signal was dropped", reaction.Hint, null);
                Note(tick, reaction.Handler, reaction.Warnings);
            }
        }

        public void Note(long tick, string source, SubmitResult result)
        {
            if (result.Error != null) Note(tick, source, result.Error);
            Note(tick, source, result.Warnings);
            Note(tick, result.Reactions);
        }

        public void Note(long tick, string source, AnswerResult result)
        {
            if (result.Error != null) Note(tick, source, result.Error);
            Note(tick, source, result.Warnings);
            Note(tick, result.Reactions);
        }

        public void Note(long tick, string source, DeliveryResult result)
        {
            if (result.Submitted != null) Note(tick, source, result.Submitted);
            if (result.Answered != null) Note(tick, source, result.Answered);
        }

        public void Note(long tick, string source, WorldException error) =>
            Add(tick, false, error.Code, source, error.Message, error.Hint, null);

        private void Note(long tick, string source, IReadOnlyList<Diagnostic> warnings)
        {
            foreach (var w in warnings)
                Add(tick, w.Severity != Severity.Error, w.Code, source, w.Message, w.Hint, w.Span.Equals(SourceSpan.None) ? null : w.Span.ToString());
        }

        private void Add(long tick, bool warning, string? code, string source, string message, string? hint, string? place)
        {
            _items.Enqueue(new BridgeProblem(++_stamp, tick, warning, code, source, message, hint, place));
            if (_items.Count > Kept) _droppedThrough = _items.Dequeue().Stamp;
        }

        public BridgeProblems Since(long since)
        {
            var items = new List<BridgeProblem>();
            foreach (var item in _items)
                if (item.Stamp > since) items.Add(item);
            return new BridgeProblems(_stamp, since < _droppedThrough, items);
        }
    }
}
