using System;
using System.Collections.Generic;
using Kesa.Engine.Diagnostics;

namespace Kesa.Engine.Script
{
    /// <summary>세션의 문장 진행: 블록과 반복, 문장 실행, 쓰기, 이름 찾기, 식 노드가 부르는 다리 (G226).</summary>
    internal sealed partial class Session
    {
        // ---- 문장 ----

        /// <summary>
        /// 스택이 <paramref name="depth"/>로 줄어들 때까지 맨 위 프레임의 문장을 하나씩 실행한다 (S2 "막힐 때까지 실행").
        /// 문장 수준의 <c>def</c> 호출은 프레임을 쌓고 이 고리가 이어서 돈다. 식은 실행 노드가 재귀로 평가한다 (G226).
        /// </summary>
        private void RunUntil(int depth)
        {
            while (_stack.Count > depth && _pendingRequest == null) Step(_stack[_stack.Count - 1]);
        }

        private void Step(Frame frame)
        {
            if (frame.Dispatch != null && CallImmediate(frame)) return;
            if (frame.Blocks.Count == 0)
            {
                Finish(NoneValue.Instance); // 끝까지 실행했다
                return;
            }
            var block = frame.Blocks[frame.Blocks.Count - 1];
            // 불러온 저장의 블록은 AST 몸체만 든다: 처음 실행할 때 이 프레임의 배치로 푼 것을 찾는다
            var code = block.Code ??= PureEvaluator.StmtBuilder.Body(block.Body, frame.Layout);
            if (block.Next < code.Length)
            {
                Exec(code[block.Next++], frame);
                return;
            }
            if (!block.IsLoop || !NextIteration(block, frame)) PopBlock(frame);
        }

        /// <summary>반복의 다음 항목으로 넘어간다. 항목이 더 없으면(<c>while</c>은 조건이 거짓이면) false.</summary>
        private bool NextIteration(Block block, Frame frame)
        {
            if (block.While != null)
            {
                // 조건 검사가 바퀴마다 한 단계를 써서, 묻지 않고 혼자 도는 while은 단계 예산이 끊는다 (G195)
                bool again;
                try
                {
                    Tick();
                    again = PureOps.Truthy(Eval(block.While.Condition, frame));
                }
                catch (ScriptRuntimeException e)
                {
                    Attach(e, block.While.Span);
                    throw;
                }
                if (!again) return false;
                block.Next = 0;
                return true;
            }

            // 엔진이 준 묶음이면 그사이 죽은 엔티티와 뗀 줄은 건너뛴다 (G88·G194)
            while (block.Live != null && block.Index + 1 < block.Count && block.Live.IsGone(block.ItemAt(block.Index + 1)))
                block.Index++;
            if (block.Index + 1 >= block.Count) return false;
            try
            {
                Tick();
            }
            catch (ScriptRuntimeException e)
            {
                Attach(e, block.Loop!.Span);
                throw;
            }
            block.Index++;
            frame.Locals.Set(SlotOf(block.Loop!.Target, frame), block.ItemAt(block.Index));
            block.Next = 0;
            return true;
        }

        /// <summary>맨 위 프레임을 끝내고 돌려준 값을 정해진 곳에 둔다.</summary>
        public void Finish(Value value)
        {
            var frame = _stack[_stack.Count - 1];
            _stack.RemoveAt(_stack.Count - 1);
            while (frame.Blocks.Count > 0) PopBlock(frame);
            switch (frame.Result)
            {
                case ResultTo.Host:
                    frame.Returned = value; // 부른 C# 쪽이 읽고 나서 돌려준다
                    return;
                case ResultTo.Local:
                    _stack[_stack.Count - 1].Locals[frame.ResultName!] = value;
                    break;
            }
            Recycle(frame);
        }

        /// <summary>문장 하나를 실행한다: 1단계를 쓰고, 오류에는 이 문장의 위치를 붙인다(안쪽 식이 이미 붙였으면 그대로).</summary>
        private void Exec(PureEvaluator.StmtNode node, Frame frame)
        {
            try
            {
                Tick();
                node.Run(this, frame);
            }
            catch (ScriptRuntimeException e)
            {
                Attach(e, node.Site.Span);
                throw;
            }
        }

        /// <summary>블록 밖의 문장 하나 (사전 조건 블록, <c>choose</c> 후보).</summary>
        private void Exec(Stmt stmt, Frame frame) => Exec(PureEvaluator.StmtBuilder.For(stmt, frame.Layout), frame);

        // ---- 블록 ----

        /// <summary>스택에서 내린 블록을 모아 두었다가 다시 쓴다 (내린 블록은 아무도 붙잡지 않는다: 멈춘 실행은 쌓여 있는 블록만 든다).</summary>
        private readonly Stack<Block> _spareBlocks = new Stack<Block>();

        public Block NewBlock(IReadOnlyList<Stmt> body, PureEvaluator.StmtNode[] code)
        {
            if (_spareBlocks.Count == 0) return new Block(body) { Code = code };
            var block = _spareBlocks.Pop();
            block.Reuse(body, code);
            return block;
        }

        public void RecycleBlock(Block block)
        {
            block.Release();
            _spareBlocks.Push(block);
        }

        /// <summary>프레임의 맨 안쪽 블록을 내린다.</summary>
        public void PopBlock(Frame frame)
        {
            var block = frame.Blocks[frame.Blocks.Count - 1];
            frame.Blocks.RemoveAt(frame.Blocks.Count - 1);
            RecycleBlock(block);
        }

        /// <summary>함수 본문의 블록 (배치마다 한 번 풀어 둔다).</summary>
        private Block BodyBlock(FunctionDef def, FrameLayout layout, int next = 0)
        {
            var code = (PureEvaluator.StmtNode[]?)layout.FunctionCode;
            if (code == null)
            {
                code = PureEvaluator.StmtBuilder.Body(def.Body, layout);
                layout.FunctionCode = code;
            }
            var block = NewBlock(def.Body, code);
            block.Next = next;
            return block;
        }
        /// <summary>
        /// 문장 수준의 호출: 한 줄 전체(<paramref name="target"/> 없음)이거나 이름 대입의 오른쪽 전체 (G117·G135).
        /// <c>def</c>는 프레임을 쌓고 돌아가 실행 고리가 이어서 실행한다. 람다·내장은 그 자리에서 값을 낸다.
        /// </summary>
        public void StatementCall(CallExpr call, Frame frame, string? target)
        {
            if (call.Callee is NameExpr name && LookupName(name, frame) is PureBuiltin pay && pay.Name == PureBuiltins.PayName)
            {
                Pay(call, frame, target);
                return;
            }
            var (callee, positional, keywords) = CallParts(call, frame);
            if (callee is FunctionValue fn && fn.Definition != null)
            {
                var called = Push(fn, null, positional, keywords, call.Span, frame.Writable, frame.UiWritable);
                called.Result = target == null ? ResultTo.Discard : ResultTo.Local;
                called.ResultName = target;
                called.Immediate = frame.Immediate;
                return;
            }
            if (callee is InnerFunction inner)
            {
                // 감싸는 함수의 original(...): 다음 안쪽 함수의 프레임을 쌓는다 (G314). 커맨드면 사전 조건을 마친 프레임 (G321)
                var called = inner.Target.IsCommand
                    ? PushInnerCommand(inner, positional, keywords, call.Span, frame)
                    : PushInner(inner, positional, keywords, call.Span, frame.Writable, frame.UiWritable);
                called.Result = target == null ? ResultTo.Discard : ResultTo.Local;
                called.ResultName = target;
                called.Immediate = frame.Immediate;
                return;
            }
            var value = Invoke(callee, positional, keywords, call.Span, frame.Writable, frame.UiWritable);
            if (callee is PureBuiltin b && b.Suspends)
            {
                // 즉시 핸들러 안에서는 시간을 막는 질문만 멈출 수 있다 (G310)
                if (frame.Immediate && b.RefusedInImmediate?.Invoke(value) is string why)
                    throw Fail(Codes.RunScript, why,
                        "an @on(..., now=True) handler runs inside the flow that emits, so it can only pause on an ask() that " +
                        "stops time; use a plain @on handler to wait (spec-gaps G310)");
                // 질문을 들고 멈춘다: 실행 고리가 돌아가고, 답은 이어서 실행할 때 target에 들어간다 (G136·G138)
                _pendingRequest = value;
                _answerTo = target;
                return;
            }
            if (target != null) frame.Locals[target] = value;
            // emit: 즉시 핸들러가 있으면 다음 문장 전에 하나씩 부른다 (G310)
            if (callee is PureBuiltin { Immediate: { } immediate } emit && positional.Count == 1)
            {
                var handlers = immediate(positional[0]);
                if (handlers.Count > 0) frame.Dispatch = new ImmediateDispatch(emit, positional[0], handlers, call.Span);
            }
        }

        /// <summary>
        /// <c>emit</c> 문장이 남긴 다음 즉시 핸들러를 쌓는다 (G310). 부를 것이 없으면 디스패치를 지우고 false.
        /// </summary>
        private bool CallImmediate(Frame frame)
        {
            var d = frame.Dispatch!;
            while (d.Next < d.Handlers.Count)
            {
                var fn = d.Handlers[d.Next++];
                var args = d.Emit.ImmediateArguments!(d.Signal, fn);
                if (args == null) continue;
                var called = Push(fn, null, Array.Empty<Value>(), args, d.Site, frame.Writable, entry: true);
                called.Result = ResultTo.Discard;
                called.Immediate = true;
                return true;
            }
            frame.Dispatch = null;
            return false;
        }

        public void ExecAssign(AssignStmt assign, Frame frame)
        {
            if (assign.Target is AttributeExpr attr)
            {
                if (frame.Writable || frame.UiWritable)
                {
                    ExecWrite(assign, attr, frame);
                    return;
                }
                // 커맨드 실행 중인데 이 프레임이 순수 모드다: 이름으로 판정할 수 없던 길로 들어왔다 (G117 런타임 안전망)
                if (_stack.Exists(f => f.Writable))
                {
                    throw Fail(Codes.RunNotWritable,
                        "'" + frame.Function.Name + "' cannot change the world here: it was reached through a function " +
                        "that only reads, or called inside an expression",
                        "only a @command body, and functions that change the world called on their own line from it, " +
                        "can write (spec-gaps G117)");
                }
                // UI 동작 안이다: 무엇에 쓰려 했는지(UI 기록인가 게임인가)에 따라 ExecWrite가 알맞은 오류를 낸다
                if (_stack.Exists(f => f.UiWritable))
                {
                    ExecWrite(assign, attr, frame);
                    return;
                }
            }
            if (!(assign.Target is NameExpr target))
                throw Fail(Codes.RunScript, "a logic function can only assign to local variables");

            if (assign.Op == "=")
            {
                // 이름 대입의 오른쪽 전체인 호출은 문장이다: 쓰기 함수의 결과를 받을 수 있다 (G135)
                if (assign.Value is CallExpr call) StatementCallTraced(call, frame, target.Name);
                else frame.Locals.Set(SlotOf(target, frame), Eval(assign.Value, frame));
                return;
            }

            int slot = SlotOf(target, frame);
            var current = frame.Locals.At(slot);
            if (current == null)
            {
                throw Fail(Codes.RunScript, "local variable '" + target.Name + "' is used before it is assigned");
            }
            // 파이썬의 리스트 +=는 그 자리에서 고쳐 별칭에도 보인다. 순수 함수의 값은 불변이므로 막는다 (G104)
            if (assign.Compound == BinaryOp.Add && current is ListValue)
            {
                throw Fail(Codes.RunScript, "'+=' cannot be used on a list in a logic function",
                    "write " + target.Name + " = " + target.Name + " + [...]: values in logic functions never change in place");
            }
            var right = Eval(assign.Value, frame);
            frame.Locals.Set(slot, Binary(assign.Compound, current, right, assign));
        }

        /// <summary>
        /// 커맨드 본문의 상태 쓰기 <c>u.Moves.left = 값</c> (G117·G132). 파이썬처럼 대상 객체(<c>u.Moves</c>)는 한 번만 평가한다.
        /// </summary>
        private void ExecWrite(AssignStmt assign, AttributeExpr attr, Frame frame)
        {
            var owner = Eval(attr.Target, frame);
            if (!(owner is IRuntimeAssignable target))
            {
                throw Fail(Codes.RunScript, owner is NoneValue
                        ? "cannot set '" + attr.Name + "' on None"
                        : "cannot set '" + attr.Name + "' on " + owner.KindName,
                    owner is NoneValue
                        ? "the component or row is missing; check it with 'is not None' first"
                        : "only members of a component or of a row can be written (spec-gaps G117)");
            }
            // UI 기록은 UI 쓰기 모드에서만, 게임 상태는 쓰기 모드에서만 (G278·G282)
            if (owner is IUiRecord ? !frame.UiWritable : !frame.Writable)
            {
                if (owner is IUiRecord) throw NotUiWritable(frame);
                throw Fail(Codes.RunNotWritable,
                    "'" + frame.Function.Name + "' cannot change the game: a @ui_action only changes ui.… and submits commands",
                    "to change the game, submit(\"Command\", ...) a command that does it (spec-gaps G278)");
            }

            Value value;
            if (assign.Op == "=")
            {
                value = Eval(assign.Value, frame);
            }
            else
            {
                // 컴포넌트의 쓸 수 있는 칸에는 리스트·맵이 없다(SCH004, G129: 리스트 칸은 const라 쓰기 거부). UI 기록 칸에는 있으므로(G277)
                // 지역 변수처럼 리스트 +=를 막는다 (G104)
                var current = owner is ISiteAttributes site
                    ? site.GetAttribute(attr)
                    : ((IRuntimeAttributes)owner).GetAttribute(attr.Name);
                if (owner is IUiRecord && assign.Compound == BinaryOp.Add && current is ListValue)
                {
                    string path = attr.Target is AttributeExpr s && s.Target is NameExpr n ? n.Name + "." + s.Name + "." + attr.Name : attr.Name;
                    throw Fail(Codes.RunScript, "'+=' cannot be used on a list in a logic function",
                        "write " + path + " = " + path + " + [...]: values in logic functions never change in place");
                }
                var right = Eval(assign.Value, frame);
                value = Binary(assign.Compound, current, right, assign);
            }
            if (target is ISiteAssignable siteTarget) siteTarget.SetAttribute(attr, value);
            else target.SetAttribute(attr.Name, value);
        }
        private ScriptRuntimeException NotUiWritable(Frame frame) =>
            Fail(Codes.RunNotWritable,
                "'" + frame.Function.Name + "' cannot change UI state here: it was reached through a function that only reads, " +
                "or called inside an expression",
                "only a @ui_action body, and functions that change UI state called on their own line from it, can change ui.… " +
                "(spec-gaps G282)");

        // ---- 식 ----

        /// <summary>식 하나를 이 프레임의 칸 배치로 만든 실행 노드로 평가한다 (G226 ⑦).</summary>
        private Value Eval(Expr expr, Frame frame) => PureEvaluator.NodeFor(expr, frame.Layout).Eval(this, frame);

        /// <summary>
        /// 이름 자리의 풀이 (G226 ①). 이 프레임의 칸 배치에 든 이름이면 칸 번호이고, 아니면 전역·내장이다. 칸 번호는 배치에만 기대므로
        /// 배치가 처음과 같은 객체면 그대로 쓴다(<c>def</c>는 함수마다 배치 하나). 전역·내장은 실행 중에 바뀌지 않으므로 같은 실행기(내장 표)·
        /// 같은 모드(전역)면 처음 찾은 값 그대로다.
        /// </summary>
        private NameSite Resolve(NameExpr site, Frame frame)
        {
            var layout = frame.Layout;
            if (site.Cache is NameSite cached && ReferenceEquals(cached.Layout, layout) &&
                (cached.Slot >= 0 || ReferenceEquals(cached.Owner, _owner) && string.Equals(cached.Mod, frame.Function.Mod)))
                return cached;
            int slot = layout.SlotOf(site.Name);
            var resolved = slot >= 0
                ? new NameSite(layout, slot, null, null, null)
                : new NameSite(layout, -1, _owner, frame.Function.Mod, LookupFree(site.Name, frame));
            site.Cache = resolved;
            return resolved;
        }

        /// <summary>대입할 지역 변수의 칸 (대입하는 이름은 언제나 그 배치에 있다, G104).</summary>
        private int SlotOf(NameExpr site, Frame frame)
        {
            if (site.Cache is NameSite cached && ReferenceEquals(cached.Layout, frame.Layout) && cached.Slot >= 0) return cached.Slot;
            return Resolve(site, frame).Slot;
        }

        private Value LookupName(NameExpr site, Frame frame)
        {
            var resolved = Resolve(site, frame);
            if (resolved.Slot < 0) return resolved.Value!;
            return frame.Locals.At(resolved.Slot) ?? throw Unassigned(site.Name);
        }

        // 함수 안 어딘가에서 대입하는 이름은 값이 없어도 지역 변수다: 같은 이름의 전역·내장으로 넘어가지 않는다 (G104)
        public ScriptRuntimeException Unassigned(string name) =>
            Fail(Codes.RunScript, "local variable '" + name + "' is used before it is assigned",
                "it is assigned somewhere in this function, so it is local everywhere in it; " +
                "assign it on every path first, or use a different name");

        /// <summary>지역 변수가 아닌 이름: 모드의 전역, 그다음 내장.</summary>
        public Value LookupFree(string name, Frame frame)
        {
            var global = _owner.Globals.Lookup(frame.Function.Mod, name);
            if (global != null) return global;
            if (_owner.Builtins.TryGetValue(name, out var builtin)) return builtin;

            var candidates = new List<string>(frame.Locals.Keys);
            candidates.RemoveAll(l => l.StartsWith("$", StringComparison.Ordinal)); // match의 숨은 변수 (G253)
            candidates.AddRange(_owner.Globals.Names(frame.Function.Mod));
            foreach (var key in _owner.Builtins.Keys) candidates.Add(key);
            throw Fail(Codes.RunScript, "name '" + name + "' is not defined", Suggest.Hint(name, candidates));
        }

        public Value Index(Value target, Value key)
        {
            switch (target)
            {
                case ListValue list:
                    if (!(key is IntValue n)) throw Fail(Codes.RunScript, "list indices must be int, not " + key.KindName);
                    long position = n.Value < 0 ? n.Value + list.Items.Count : n.Value;
                    if (position < 0 || position >= list.Items.Count)
                        throw Fail(Codes.RunScript, "list index " + n.Value + " is out of range (length " + list.Items.Count + ")");
                    return list.Items[(int)position];

                case DictValue dict:
                    var found = PureOps.FindKey(dict, key);
                    if (found == null) throw Fail(Codes.RunScript, "the dict has no such key");
                    return found;

                case IRuntimeIndexable indexable:
                    return indexable.GetItem(key);
            }
            throw Fail(Codes.RunScript, "'" + target.KindName + "' cannot be indexed");
        }

        /// <summary><c>x = f(...)</c>의 호출을 <see cref="Eval"/>처럼 단계를 세고 오류 위치를 붙여 실행한다.</summary>
        public void StatementCallTraced(CallExpr call, Frame frame, string target)
        {
            try
            {
                Tick();
                StatementCall(call, frame, target);
            }
            catch (ScriptRuntimeException e)
            {
                Attach(e, call.Span);
                throw;
            }
        }

        /// <summary>문장 수준 호출의 부를 것과 인자 (식 안의 호출과 같은 실행 노드로 계산한다).</summary>
        private (Value Callee, IReadOnlyList<Value> Positional, IReadOnlyDictionary<string, Value> Keywords) CallParts(CallExpr call, Frame frame)
        {
            var callee = PureEvaluator.CallParts(call, this, frame, out var positional, out var keywords);
            return (callee, positional, keywords);
        }

        /// <param name="site">연산자를 쓴 식이나 복합 대입: 지원하지 않는 연산자의 오류 문구에 원래 글자를 쓴다.</param>
        public Value Binary(BinaryOp op, Value left, Value right, Node site) =>
            PureOps.Binary(op, left, right) ??
            throw Fail(Codes.RunScript, "operator '" + Symbol(site) + "' is not supported in a logic function", "see spec-gaps G96");

        private static string Symbol(Node site) =>
            site is AssignStmt assign ? assign.Op.Substring(0, assign.Op.Length - 1) : ((BinaryExpr)site).Op;
    }
}
