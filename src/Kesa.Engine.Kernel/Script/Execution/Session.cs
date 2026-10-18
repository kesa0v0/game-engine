using System;
using System.Collections.Generic;
using Kesa.Engine.Diagnostics;

namespace Kesa.Engine.Script
{
    /// <summary>
    /// <see cref="PureEvaluator"/>의 실행 하나: 프레임 스택, 단계 계량기, 멈춤과 이어 하기, 오류 위치 붙이기.
    /// 호출은 <c>Session.Calls.cs</c>, 커맨드 사전 조건과 비용은 <c>Session.Preconditions.cs</c>, 문장 진행은 <c>Session.Stepping.cs</c>.
    /// 식·문장 노드는 <see cref="PureEvaluator"/> 안에 두고 <see cref="PureEvaluator.NodeFor"/>·<see cref="PureEvaluator.CallParts"/>로 닿는다.
    /// </summary>
    internal sealed partial class Session
    {
        private readonly PureEvaluator _owner;
        private readonly List<Frame> _stack = new List<Frame>();
        private StepMeter _meter;

        /// <param name="meter">null이면 쓰기 전에 <see cref="Begin"/>으로 준다.</param>
        public Session(PureEvaluator owner, StepMeter? meter)
        {
            _owner = owner;
            _meter = meter!;
        }

        /// <summary>다시 쓰는 세션의 새 실행을 시작한다.</summary>
        public void Begin(StepMeter meter) => _meter = meter;

        /// <summary>실행이 끝난 세션을 비운다(다음 실행이 앞 실행의 흔적을 보지 않게, 붙잡은 값을 놓게).</summary>
        public void Clear()
        {
            _stack.Clear();
            _costs.Clear();
            _pendingRequest = null;
            _answerTo = null;
            _suspended = null;
            _meter = null!;
        }

        // ---- 멈춤 ----

        /// <summary>문장 수준의 <c>ask</c>가 낸 질문. 있으면 실행 고리가 멈춘다.</summary>
        private Value? _pendingRequest;

        /// <summary>사전 조건 블록의 <c>cost</c>들, 적힌 순서대로. 본문이 시작될 때 이 금액을 뺀다 (G185).</summary>
        private readonly List<CostEntry> _costs = new List<CostEntry>();

        private sealed class CostEntry
        {
            public CostEntry(IRuntimeSlots owner, string name, Value amount, SourceSpan span)
            {
                Owner = owner;
                Name = name;
                Amount = amount;
                Span = span;
            }

            public IRuntimeSlots Owner { get; }
            public string Name { get; }
            public Value Amount { get; }
            public SourceSpan Span { get; }
        }

        private string? _answerTo;

        /// <summary>멈춘 실행. 멈출 때 스택에서 떼어 둔다.</summary>
        private PureEvaluator.Suspended? _suspended;

        /// <summary>가장 바깥 실행 고리가 멈췄으면 스택 전체를 떼어 멈춘 실행으로 만든다.</summary>
        private void Detach()
        {
            if (_pendingRequest == null) return;
            _suspended = new PureEvaluator.Suspended(new List<Frame>(_stack), _answerTo, _pendingRequest);
            _stack.Clear();
            _pendingRequest = null;
            _answerTo = null;
        }

        public PureEvaluator.Suspended? TakeSuspended()
        {
            var s = _suspended;
            _suspended = null;
            return s;
        }

        /// <summary>멈춘 실행의 사본을 스택에 올리고 답을 받을 변수에 넣는다.</summary>
        public void Restore(PureEvaluator.Suspended from, Value answer)
        {
            _stack.AddRange(CopyStack(from.Frames));
            if (from.AnswerTo != null) _stack[_stack.Count - 1].Locals[from.AnswerTo] = answer;
        }

        /// <summary>
        /// 프레임 스택의 사본. 람다는 만든 프레임의 지역 변수 사전을 잡고 늦게 읽으므로(G104), 사본 안의 람다는 사본의 사전을
        /// 잡도록 바꾼다. 원본 람다 하나에 사본 하나라 같은 람다를 든 두 변수는 사본에서도 같은 객체다 (G143).
        /// 이미 끝난 함수의 사전은 더 바뀌지 않지만 스택의 사전을 잡은 람다를 들 수 있어서 함께 옮긴 사본을 쓴다.
        /// </summary>
        private static List<Frame> CopyStack(List<Frame> frames)
        {
            var copies = new List<Frame>(frames.Count);
            foreach (var frame in frames) copies.Add(frame.Copy());
            var closures = new Dictionary<ClosureValue, ClosureValue>();
            var envs = new Dictionary<LocalTable, LocalTable>();

            LocalTable Env(LocalTable captured)
            {
                for (int i = 0; i < frames.Count; i++)
                    if (ReferenceEquals(captured, frames[i].Locals)) return copies[i].Locals;
                if (envs.TryGetValue(captured, out var copied)) return copied;
                copied = new LocalTable(captured.Layout);
                envs.Add(captured, copied); // 채우기 전에 등록한다: 사전이 자기를 잡은 람다를 들 수 있다
                var names = new SortedSet<string>(StringComparer.Ordinal);
                foreach (var pair in captured) names.Add(pair.Key);
                foreach (var name in names) copied[name] = Remap(captured[name]);
                return copied;
            }

            Value Remap(Value value)
            {
                switch (value)
                {
                    case ClosureValue closure:
                        if (closures.TryGetValue(closure, out var copy)) return copy;
                        // 사전을 옮기는 동안 같은 람다를 다시 만날 수 있으니 먼저 만들 자리를 잡을 수 없다: 사전부터 옮긴다
                        var env = Env(closure.Captured);
                        if (closures.TryGetValue(closure, out copy)) return copy;
                        copy = new ClosureValue(closure.Function, env);
                        closures.Add(closure, copy);
                        return copy;
                    case ListValue list:
                        return new ListValue(RemapAll(list.Items));
                    case DictValue dict:
                        var entries = new List<DictItem>(dict.Items.Count);
                        foreach (var item in dict.Items) entries.Add(new DictItem(Remap(item.Key), Remap(item.Value), item.Span));
                        return new DictValue(entries);
                    case TextValue text:
                        var args = new SortedDictionary<string, Value>(StringComparer.Ordinal);
                        foreach (var pair in text.Args) args[pair.Key] = Remap(pair.Value);
                        return new TextValue(text.Key, args);
                    case IValueContainer container:
                        return container.MapValues(Remap);
                }
                // 저장(SaveScriptValue)이 들어가는 곳은 여기도 모두 들어간다: 두 길이 같아야 C1이다
                return value;
            }

            List<Value> RemapAll(IReadOnlyList<Value> items)
            {
                var result = new List<Value>(items.Count);
                foreach (var item in items) result.Add(Remap(item));
                return result;
            }

            foreach (var copy in copies)
            {
                foreach (var name in new List<string>(copy.Locals.Keys)) copy.Locals[name] = Remap(copy.Locals[name]);
                foreach (var block in copy.Blocks)
                    if (block.Items != null) block.Items = RemapAll(block.Items);
            }
            return copies;
        }

        public void RunToEnd()
        {
            try
            {
                RunUntil(0);
                Detach();
            }
            finally
            {
                Truncate(0);
            }
        }

        // ---- 오류 ----

        private static bool HasSpan(ScriptRuntimeException e) => e.Span.Line > 0;

        public ScriptRuntimeException Fail(string code, string message, string? hint = null) =>
            new ScriptRuntimeException(code, message, hint);

        /// <summary>
        /// 이 실행 단위의 프레임을 오류의 흔적에 한 번 덧붙인다. 위치가 아직 없으면 이 식이 오류 위치다.
        /// 이미 있으면 오류는 안쪽 실행(이 식이 읽은 다른 <c>derived</c>)에서 났으므로, 이 식의 위치는 가장 안쪽
        /// 프레임에 "at …"으로 붙여 어디서 읽었는지 남긴다.
        /// </summary>
        public void Attach(ScriptRuntimeException e, SourceSpan span)
        {
            if (ReferenceEquals(e.TracedBy, this)) return;
            e.TracedBy = this;
            bool nested = HasSpan(e);
            if (!nested) e.Span = span;
            for (int i = _stack.Count - 1; i >= 0; i--)
            {
                var frame = _stack[i];
                string line = frame.Function.Name;
                if (nested && i == _stack.Count - 1) line += " at " + span;
                if (frame.CallSite.Line > 0) line += " (called at " + frame.CallSite + ")";
                e.AddTrace(line);
            }
        }

        public PureEvaluator Owner => _owner;

        /// <summary>오류에 노드의 위치를 붙여 돌려준다 (감싸지 않는 노드가 스스로 낸 오류).</summary>
        public ScriptRuntimeException Placed(ScriptRuntimeException e, PureEvaluator.ExprNode node)
        {
            Attach(e, node.Span);
            return e;
        }

        /// <summary>감싸지 않는 노드의 한 단계: 넘치면 그 노드의 위치를 붙인다.</summary>
        public void Tick(PureEvaluator.ExprNode node)
        {
            if (++_meter.Used > _meter.Budget) throw Placed(BudgetExceeded(), node);
        }

        public void Tick()
        {
            if (++_meter.Used > _meter.Budget) throw BudgetExceeded();
        }

        private ScriptRuntimeException BudgetExceeded() =>
            Fail(Codes.RunBudget, "the function used more than " + _meter.Budget + " evaluation steps",
                "a loop may be too large or endless; the budget is shared by everything one read computes, " +
                "including the derived values it reads");
    }
}
