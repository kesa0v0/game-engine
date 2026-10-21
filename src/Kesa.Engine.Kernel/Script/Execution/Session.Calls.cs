using System;
using System.Collections.Generic;
using Kesa.Engine.Diagnostics;

namespace Kesa.Engine.Script
{
    /// <summary>세션의 함수 호출: 인자 묶기, 재귀 검사, 프레임 만들기·다시 쓰기, 지역 변수 표 (G226 호출 규약).</summary>
    internal sealed partial class Session
    {
        // ---- 호출 ----

        /// <param name="writable">
        /// 부른 쪽이 쓰기 모드 프레임이고 이 호출이 문장일 때 참. 그래도 부르는 함수가 쓰기 함수여야 쓰기 모드가 된다 (G117).
        /// </param>
        /// <param name="uiWritable">부른 쪽이 UI 쓰기 모드 프레임이고 이 호출이 문장일 때 참 (G282, <paramref name="writable"/>과 같은 규칙).</param>
        public Value Invoke(Value function, IReadOnlyList<Value> positional,
            IReadOnlyDictionary<string, Value> keywords, SourceSpan site, bool writable = false, bool uiWritable = false)
        {
            switch (function)
            {
                case FunctionValue f:
                    return CallFunction(f, null, positional, keywords, site, writable, uiWritable);
                case ClosureValue c:
                    return CallFunction(c.Function, c.Captured, positional, keywords, site, writable, uiWritable);
                case PureBuiltin b:
                    if (b.WritesUi && !uiWritable)
                    {
                        throw Fail(Codes.RunNotWritable,
                            b.Name + "() asks the host to submit a command, so it cannot be called here",
                            "call it on its own line in a @ui_action or in a function that changes UI state called on its own " +
                            "line from it (spec-gaps G282)");
                    }
                    if (b.Writes && !writable)
                    {
                        throw Fail(Codes.RunNotWritable,
                            b.Name + "() " + PureBuiltins.EffectPhrase(b.Name) + ", so it cannot be called here",
                            "call it on its own line (or as 'x = " + b.Name + "(...)') in a @command body or in a " +
                            "function that changes the world called on its own line from it (spec-gaps G117, G135)");
                    }
                    // 본문 프레임이 스택에 있어야 한다: derived·조건·입구는 따로 도는 실행이고, 사전 조건은 본문이 열리기 전이다 (G178)
                    if (b.Random && !_stack.Exists(f => f.Writable))
                    {
                        throw Fail(Codes.RunNotWritable, b.Name + "() draws a random number, so it cannot be called here",
                            "random numbers can only be drawn in a @command, @on or @when body and the functions it calls; " +
                            "derived values, conditions and preconditions must give the same answer every time (spec-gaps G178)");
                    }
                    if (b.CallerBody != null) return b.CallerBody(TopMod, positional, keywords);
                    if (b.KeywordBody != null) return b.KeywordBody(positional, keywords);
                    if (keywords.Count > 0) throw Fail(Codes.RunScript, b.Name + "() does not take keyword arguments");
                    return b.Body(positional);
                case HostCallable h:
                    // 로드 검사가 이름으로 부른 곳은 막았다. 함수 값으로 부르는 길의 방어선 (G216)
                    if (h.Writes && !writable)
                    {
                        throw Fail(Codes.RunNotWritable, h.Name + "() changes the world, so it cannot be called here",
                            "call it on its own line (or as 'x = " + h.Name + "(...)') in a @command body or in a " +
                            "function that changes the world called on its own line from it (spec-gaps G216)");
                    }
                    return h.Call(positional, keywords);
            }
            throw Fail(Codes.RunScript, "'" + function.KindName + "' is not callable");
        }

        private static bool SameFunction(FunctionValue a, FunctionValue b) =>
            ReferenceEquals(a.Definition, b.Definition) && ReferenceEquals(a.Lambda, b.Lambda);

        /// <summary>C# 쪽에서 끝까지 실행하는 호출 (호스트 실행, 식 안의 호출). 돌려준 값을 받는다.</summary>
        private Value CallFunction(FunctionValue fn, LocalTable? captured,
            IReadOnlyList<Value> positional, IReadOnlyDictionary<string, Value> keywords, SourceSpan site,
            bool writable, bool uiWritable)
        {
            int depth = _stack.Count;
            var newFrame = Push(fn, captured, positional, keywords, site, writable, uiWritable);
            newFrame.Result = ResultTo.Host;
            try
            {
                Value result;
                if (fn.Lambda != null)
                {
                    result = Eval(fn.Lambda.Body, newFrame);
                    _stack.RemoveAt(_stack.Count - 1);
                }
                else
                {
                    RunUntil(depth);
                    // 식 안의 호출은 쓰기 모드가 아니라 ask가 먼저 거부된다: 여기 오면 엔진 결함이다 (G136)
                    if (_pendingRequest != null)
                        throw new InvalidOperationException("a call inside an expression stopped at ask()");
                    result = newFrame.Returned;
                }
                Recycle(newFrame); // 끝까지 실행했다 (오류로 빠져나간 프레임은 다시 쓰지 않는다)
                return result;
            }
            finally
            {
                Truncate(depth);
            }
        }

        /// <summary>재귀를 검사하고 인자를 묶은 프레임을 스택에 넣는다. <c>def</c>면 몸체 블록에서 시작한다.</summary>
        private Frame Push(FunctionValue fn, LocalTable? captured,
            IReadOnlyList<Value> positional, IReadOnlyDictionary<string, Value> keywords, SourceSpan site,
            bool writable, bool uiWritable = false, bool entry = false)
        {
            // 커맨드는 제출로만 실행된다 (G118). 이름으로 잡히지 않은 길(함수 값)의 마지막 방어선. 즉시 핸들러는 emit이 부른다 (G310)
            if (fn.IsEntry && !entry)
            {
                throw Fail(Codes.RunScript, "'" + fn.Name + "' is a " + fn.EntryKind + ", so logic cannot call it",
                    "a command only runs when it is submitted; move the shared part into a plain function (spec-gaps G118)");
            }
            var newFrame = Enter(fn, captured, positional, keywords);
            newFrame.CallSite = site;
            newFrame.Writable = writable && (fn.Writes || entry);
            newFrame.UiWritable = uiWritable && fn.WritesUi;
            if (fn.Definition != null) newFrame.Blocks.Add(BodyBlock(fn.Definition, newFrame.Layout));
            _stack.Add(newFrame);
            return newFrame;
        }

        /// <summary>오류로 빠져나갈 때 이 실행 단위가 넣은 프레임을 걷어 낸다.</summary>
        private void Truncate(int depth)
        {
            while (_stack.Count > depth) _stack.RemoveAt(_stack.Count - 1);
        }

        /// <summary>재귀를 검사하고 인자를 매개변수에 묶은 새 프레임을 만든다 (스택에는 아직 넣지 않는다).</summary>
        private Frame Enter(FunctionValue fn, LocalTable? captured,
            IReadOnlyList<Value> positional, IReadOnlyDictionary<string, Value> keywords)
        {
            for (int k = 0; k < _stack.Count; k++)
            {
                if (!SameFunction(_stack[k].Function, fn)) continue;
                var chain = new List<string>();
                bool inChain = false;
                foreach (var f in _stack)
                {
                    if (SameFunction(f.Function, fn)) inChain = true;
                    if (inChain) chain.Add(f.Function.Name);
                }
                chain.Add(fn.Name);
                throw Fail(Codes.RunCycle, "function '" + fn.Name + "' calls itself: " + string.Join(" -> ", chain),
                    "recursion is not allowed in logic functions");
            }

            var parameters = fn.Parameters;
            if (positional.Count > parameters.Count)
            {
                throw Fail(Codes.RunScript, fn.Name + "() takes " + parameters.Count + " argument" +
                                            (parameters.Count == 1 ? "" : "s") + ", got " + positional.Count);
            }

            var locals = NewLocals(fn, captured);
            // 인자는 매개변수의 칸에 곧장 넣는다 (칸 번호는 배치마다 한 번 정한다)
            var slots = locals.Layout.SlotsOf(parameters);
            for (int i = 0; i < positional.Count; i++) locals.Set(slots[i], positional[i]);
            if (keywords.Count == 0)
            {
                // 위치 인자만이면(대부분의 호출) 앞에서부터 채워지므로 빠진 것은 그 뒤다: 집합을 만들 필요가 없다
                if (positional.Count < parameters.Count)
                    throw Fail(Codes.RunScript, fn.Name + "() is missing the argument '" + parameters[positional.Count].Name + "'");
                return NewFrame(fn, locals);
            }
            var bound = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < positional.Count; i++) bound.Add(parameters[i].Name);
            foreach (var pair in keywords)
            {
                int at = -1;
                for (int i = 0; i < parameters.Count && at < 0; i++)
                    if (parameters[i].Name == pair.Key) at = i;
                if (at < 0) throw Fail(Codes.RunScript, fn.Name + "() has no parameter '" + pair.Key + "'");
                if (!bound.Add(pair.Key))
                    throw Fail(Codes.RunScript, fn.Name + "() got several values for '" + pair.Key + "'");
                locals.Set(slots[at], pair.Value);
            }
            foreach (var p in parameters)
            {
                if (!bound.Contains(p.Name))
                    throw Fail(Codes.RunScript, fn.Name + "() is missing the argument '" + p.Name + "'");
            }

            return NewFrame(fn, locals);
        }

        /// <summary>
        /// 끝난 프레임을 모아 두었다가 다시 쓴다 (부르기마다 프레임과 블록 목록을 새로 만들지 않는다). 끝난 프레임은 아무도 붙잡지 않는다:
        /// 람다는 프레임이 아니라 지역 변수 표를 잡고, 멈춘 실행은 끝나지 않은 프레임만 든다.
        /// </summary>
        private readonly Stack<Frame> _spareFrames = new Stack<Frame>();

        private Frame NewFrame(FunctionValue fn, LocalTable locals)
        {
            if (_spareFrames.Count == 0) return new Frame(fn, locals);
            var frame = _spareFrames.Pop();
            frame.Reuse(fn, locals);
            return frame;
        }

        /// <summary>끝나서 스택에서 내린 프레임을 다시 쓰도록 돌려준다.</summary>
        private void Recycle(Frame frame)
        {
            frame.Release();
            _spareFrames.Push(frame);
        }

        /// <summary>
        /// 새 프레임의 빈 지역 변수 표. 람다는 대입할 수 없으니 지역 변수는 매개변수와 만든 이름 공간의 지역 변수다: 만든 이름 공간의 표를
        /// 지금 값 그대로 옮긴다(늦게 묶임, G104).
        /// </summary>
        private LocalTable NewLocals(FunctionValue fn, LocalTable? captured)
        {
            if (fn.Definition != null) return new LocalTable(PureEvaluator.LayoutOf(fn.Definition));
            var lambda = fn.Lambda!;
            if (captured == null) return new LocalTable(PureEvaluator.BareLambdaLayout(lambda));
            if (!captured.Layout.TryChild(lambda, out var layout))
                layout = captured.Layout.Child(lambda, ParameterNames(lambda.Parameters));
            return captured.ChildTable(layout);
        }

        private static List<string> ParameterNames(IReadOnlyList<Parameter> parameters)
        {
            var names = new List<string>(parameters.Count);
            foreach (var p in parameters) names.Add(p.Name);
            return names;
        }
    }
}
