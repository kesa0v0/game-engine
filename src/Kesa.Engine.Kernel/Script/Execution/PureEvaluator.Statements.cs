using System.Collections.Generic;
using Kesa.Engine.Diagnostics;

namespace Kesa.Engine.Script
{
    public sealed partial class PureEvaluator
    {
        /// <summary>
        /// 문장 하나를 칸 배치 하나에서 풀어 만든 실행 노드 (G226 ⑥⑦). 문장은 여전히 블록 자료로 한 문장씩 실행된다(멈춤과 저장, S2): 노드는
        /// 문장 하나의 일만 하고, 몸체가 있는 문장(<c>if</c>·<c>for</c>·<c>while</c>)은 풀린 몸체를 든 블록을 쌓는다. 블록은 저장을 위해 AST 몸체도
        /// 함께 든다(저장 형식은 문장 경로 그대로다).
        /// <para>단계 세기와 오류 위치는 실행 고리(<see cref="Session.Exec(StmtNode, Frame)"/>)가 문장마다 한다.</para>
        /// </summary>
        internal abstract class StmtNode
        {
            protected StmtNode(Stmt site) => Site = site;

            public Stmt Site { get; }

            public abstract void Run(Session s, Frame f);
        }

        /// <summary>AST 문장의 실행 노드를 칸 배치마다 하나 기억한다 (<see cref="Stmt.Compiled"/>).</summary>
        private sealed class CompiledStmt
        {
            public CompiledStmt(FrameLayout layout, StmtNode node)
            {
                Layout = layout;
                Node = node;
            }

            public FrameLayout Layout { get; }
            public StmtNode Node { get; }
        }

        /// <summary>문장과 문장 목록을 칸 배치에서 실행 노드로 만든다.</summary>
        internal static class StmtBuilder
        {
            public static StmtNode For(Stmt stmt, FrameLayout layout)
            {
                if (stmt.Compiled is CompiledStmt site && ReferenceEquals(site.Layout, layout)) return site.Node;
                var node = Build(stmt, layout);
                stmt.Compiled = new CompiledStmt(layout, node);
                return node;
            }

            /// <summary>문장 목록(몸체)의 실행 노드. 배치마다 목록 객체로 한 번 만든다 (불러온 저장의 블록도 같은 것을 찾는다).</summary>
            public static StmtNode[] Body(IReadOnlyList<Stmt> body, FrameLayout layout)
            {
                if (layout.TryCode(body, out var known)) return (StmtNode[])known;
                var code = new StmtNode[body.Count];
                layout.SetCode(body, code); // 채우기 전에 등록한다: 몸체가 자기를 다시 찾지는 않지만 순서를 단순하게
                for (int i = 0; i < code.Length; i++) code[i] = For(body[i], layout);
                return code;
            }

            private static StmtNode Build(Stmt stmt, FrameLayout layout)
            {
                switch (stmt)
                {
                    case ExprStmt es:
                        return es.Expr is CallExpr call
                            ? new CallStmtNode(es, call)
                            : (StmtNode)new ExprStmtNode(es, NodeBuilder.For(es.Expr, layout));
                    case AssignStmt assign:
                        if (assign.Target is NameExpr name && assign.Op == "=")
                        {
                            // 이름 대입의 오른쪽 전체인 호출은 문장이다: 쓰기 함수의 결과를 받을 수 있다 (G135)
                            if (assign.Value is CallExpr valueCall) return new CallAssignNode(assign, valueCall, name.Name);
                            int slot = layout.SlotOf(name.Name);
                            if (slot >= 0) return new LocalAssignNode(assign, slot, NodeBuilder.For(assign.Value, layout));
                        }
                        return new OtherAssignNode(assign);
                    case IfStmt ifs:
                        var conditions = new ExprNode[ifs.Branches.Count];
                        var bodies = new StmtNode[ifs.Branches.Count][];
                        for (int i = 0; i < conditions.Length; i++)
                        {
                            conditions[i] = NodeBuilder.For(ifs.Branches[i].Condition, layout);
                            bodies[i] = Body(ifs.Branches[i].Body, layout);
                        }
                        return new IfNode(ifs, conditions, bodies, ifs.OrElse == null ? null : Body(ifs.OrElse, layout));
                    case ForStmt loop:
                        return new ForNode(loop, NodeBuilder.For(loop.Iterable, layout), Body(loop.Body, layout));
                    case WhileStmt loop:
                        return new WhileNode(loop, Body(loop.Body, layout));
                    case ReturnStmt ret:
                        return new ReturnNode(ret, ret.Value == null ? null : NodeBuilder.For(ret.Value, layout));
                    case PassStmt pass:
                        return new PassNode(pass);
                    case BreakStmt brk:
                        return new BreakNode(brk);
                    case ContinueStmt cont:
                        return new ContinueNode(cont);
                }
                return new UnsupportedStmtNode(stmt);
            }
        }

        private sealed class ExprStmtNode : StmtNode
        {
            private readonly ExprNode _expr;

            public ExprStmtNode(ExprStmt site, ExprNode expr) : base(site) => _expr = expr;

            public override void Run(Session s, Frame f) => _expr.Eval(s, f);
        }

        /// <summary>한 줄 전체인 호출 (G117·G135): <c>def</c>는 프레임을 쌓고, 람다·내장은 그 자리에서 값을 낸다.</summary>
        private sealed class CallStmtNode : StmtNode
        {
            private readonly CallExpr _call;

            public CallStmtNode(ExprStmt site, CallExpr call) : base(site) => _call = call;

            public override void Run(Session s, Frame f) => s.StatementCall(_call, f, null);
        }

        /// <summary><c>x = f(...)</c>: 호출은 식처럼 단계를 세고 오류 위치를 붙인다.</summary>
        private sealed class CallAssignNode : StmtNode
        {
            private readonly CallExpr _call;
            private readonly string _target;

            public CallAssignNode(AssignStmt site, CallExpr call, string target) : base(site)
            {
                _call = call;
                _target = target;
            }

            public override void Run(Session s, Frame f) => s.StatementCallTraced(_call, f, _target);
        }

        /// <summary>지역 변수 대입 <c>x = 식</c>.</summary>
        private sealed class LocalAssignNode : StmtNode
        {
            private readonly int _slot;
            private readonly ExprNode _value;

            public LocalAssignNode(AssignStmt site, int slot, ExprNode value) : base(site)
            {
                _slot = slot;
                _value = value;
            }

            public override void Run(Session s, Frame f) => f.Locals.Set(_slot, _value.Eval(s, f));
        }

        /// <summary>복합 대입(<c>x += 1</c>)과 상태 쓰기(<c>u.Moves.left = 3</c>).</summary>
        private sealed class OtherAssignNode : StmtNode
        {
            private readonly AssignStmt _assign;

            public OtherAssignNode(AssignStmt site) : base(site) => _assign = site;

            public override void Run(Session s, Frame f) => s.ExecAssign(_assign, f);
        }

        private sealed class IfNode : StmtNode
        {
            private readonly IfStmt _site;
            private readonly ExprNode[] _conditions;
            private readonly StmtNode[][] _bodies;
            private readonly StmtNode[]? _orElse;

            public IfNode(IfStmt site, ExprNode[] conditions, StmtNode[][] bodies, StmtNode[]? orElse) : base(site)
            {
                _site = site;
                _conditions = conditions;
                _bodies = bodies;
                _orElse = orElse;
            }

            public override void Run(Session s, Frame f)
            {
                for (int i = 0; i < _conditions.Length; i++)
                {
                    if (!PureOps.Truthy(_conditions[i].Eval(s, f))) continue;
                    f.Blocks.Add(s.NewBlock(_site.Branches[i].Body, _bodies[i]));
                    return;
                }
                if (_orElse != null) f.Blocks.Add(s.NewBlock(_site.OrElse!, _orElse));
            }
        }

        /// <summary>
        /// 반복할 항목을 정해 반복 블록을 쌓는다. 항목은 시작할 때 굳는다(목록은 펼치고 range는 그대로 둔다). 첫 항목으로 넘어가는 것은 실행 고리가 한다.
        /// </summary>
        private sealed class ForNode : StmtNode
        {
            private readonly ForStmt _loop;
            private readonly ExprNode _iterable;
            private readonly StmtNode[] _body;

            public ForNode(ForStmt loop, ExprNode iterable, StmtNode[] body) : base(loop)
            {
                _loop = loop;
                _iterable = iterable;
                _body = body;
            }

            public override void Run(Session s, Frame f)
            {
                var iterable = _iterable.Eval(s, f);
                var block = s.NewBlock(_loop.Body, _body);
                block.Loop = _loop;
                switch (iterable)
                {
                    case ListValue list:
                        block.Items = list.Items;
                        break;
                    case DictValue dict:
                        var keys = new List<Value>(dict.Items.Count);
                        foreach (var item in dict.Items) keys.Add(item.Key);
                        block.Items = keys;
                        break;
                    case RangeValue range:
                        block.Range = range;
                        break;
                    case IRuntimeIterable runtime:
                        block.Items = runtime.Iterate();
                        block.Live = runtime as IRuntimeLiveCollection;
                        break;
                    default:
                        s.RecycleBlock(block);
                        throw s.Fail(Codes.RunScript, "'" + iterable.KindName + "' is not iterable",
                            "loop over a list, a dict or range(...)");
                }
                block.Count = block.Range?.Count ?? block.Items!.Count;
                block.Next = _body.Length;
                f.Blocks.Add(block);
            }
        }

        private sealed class WhileNode : StmtNode
        {
            private readonly WhileStmt _loop;
            private readonly StmtNode[] _body;

            public WhileNode(WhileStmt loop, StmtNode[] body) : base(loop)
            {
                _loop = loop;
                _body = body;
            }

            public override void Run(Session s, Frame f)
            {
                // 끝에 선 빈 몸체를 쌓는다: 첫 조건 검사도 실행 고리가 NextIteration에서 한다
                var block = s.NewBlock(_loop.Body, _body);
                block.While = _loop;
                block.Next = _body.Length;
                f.Blocks.Add(block);
            }
        }

        private sealed class ReturnNode : StmtNode
        {
            private readonly ExprNode? _value;

            public ReturnNode(ReturnStmt site, ExprNode? value) : base(site) => _value = value;

            public override void Run(Session s, Frame f) => s.Finish(_value == null ? NoneValue.Instance : _value.Eval(s, f));
        }

        private sealed class PassNode : StmtNode
        {
            public PassNode(PassStmt site) : base(site)
            {
            }

            public override void Run(Session s, Frame f)
            {
            }
        }

        private sealed class BreakNode : StmtNode
        {
            public BreakNode(BreakStmt site) : base(site)
            {
            }

            // 가장 안쪽 반복의 몸체까지 걷어 낸다 (반복 밖의 break는 파서가 막는다)
            public override void Run(Session s, Frame f)
            {
                while (!f.Blocks[f.Blocks.Count - 1].IsLoop) s.PopBlock(f);
                s.PopBlock(f);
            }
        }

        private sealed class ContinueNode : StmtNode
        {
            public ContinueNode(ContinueStmt site) : base(site)
            {
            }

            public override void Run(Session s, Frame f)
            {
                while (!f.Blocks[f.Blocks.Count - 1].IsLoop) s.PopBlock(f);
                var body = f.Blocks[f.Blocks.Count - 1];
                body.Next = body.Body.Count;
            }
        }

        private sealed class UnsupportedStmtNode : StmtNode
        {
            public UnsupportedStmtNode(Stmt site) : base(site)
            {
            }

            public override void Run(Session s, Frame f) =>
                throw s.Fail(Codes.RunScript, "this statement is not supported in a logic function");
        }
    }
}
