using System;
using System.Collections.Generic;
using Kesa.Engine.Diagnostics;

namespace Kesa.Engine.Script
{
    /// <summary>
    /// 실행 중인 문장 목록 하나와 다음에 실행할 문장의 번호. <c>for</c>의 몸체면 반복 상태(남은 항목과 위치)도 든다.
    /// </summary>
    internal sealed class Block
    {
        public Block(IReadOnlyList<Stmt> body, int next = 0)
        {
            Body = body;
            Next = next;
        }

        /// <summary>AST 문장 목록 (저장이 문장 경로를 적고 다시 찾는다).</summary>
        public IReadOnlyList<Stmt> Body { get; private set; }

        /// <summary>
        /// <see cref="Body"/>를 이 프레임의 칸 배치에서 풀어 둔 실행 노드 (G226). 실행 고리가 채운다: 불러온 저장의 블록은 처음 실행할 때 찾는다.
        /// </summary>
        internal PureEvaluator.StmtNode[]? Code { get; set; }

        public int Next { get; set; }

        /// <summary>다시 쓰는 블록을 새 몸체로 채운다 (나머지는 <see cref="Release"/>가 비웠다).</summary>
        internal void Reuse(IReadOnlyList<Stmt> body, PureEvaluator.StmtNode[] code)
        {
            Body = body;
            Code = code;
        }

        /// <summary>스택에서 내린 블록이 붙잡은 것을 놓는다 (다시 쓰기 전).</summary>
        internal void Release()
        {
            Body = null!;
            Code = null;
            Next = 0;
            Loop = null;
            While = null;
            Items = null;
            Range = null;
            Live = null;
            Count = 0;
            Index = -1;
        }

        /// <summary>이 블록이 <c>for</c>의 몸체면 그 문장.</summary>
        public ForStmt? Loop { get; set; }

        /// <summary>이 블록이 <c>while</c>의 몸체면 그 문장. 바퀴마다 조건을 다시 평가한다 (G195).</summary>
        public WhileStmt? While { get; set; }

        /// <summary><c>break</c>·<c>continue</c>가 찾는 반복의 몸체인가.</summary>
        public bool IsLoop => Loop != null || While != null;

        /// <summary>반복 항목: <see cref="Range"/>가 아니면 시작할 때 펼친 목록.</summary>
        public IReadOnlyList<Value>? Items { get; set; }

        public RangeValue? Range { get; set; }

        /// <summary>엔진이 준 묶음을 돌면 그 묶음: 한 바퀴마다 사라진 항목을 건너뛴다 (G88·G194).</summary>
        public IRuntimeLiveCollection? Live { get; set; }

        public long Count { get; set; }

        /// <summary>지금 실행 중인 항목의 위치 (아직 시작 전이면 -1).</summary>
        public long Index { get; set; } = -1;

        public Value ItemAt(long i) => Range != null ? new IntValue(Range.At(i)) : Items![(int)i];

        public Block Copy() => new Block(Body, Next)
        {
            Code = Code,
            Loop = Loop,
            While = While,
            Items = Items,
            Range = Range,
            Live = Live,
            Count = Count,
            Index = Index,
        };
    }
}
