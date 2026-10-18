using System;
using System.Collections.Generic;
using System.Globalization;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Schema;
using Kesa.Engine.Script;

namespace Kesa.Engine.Runtime
{
    /// <summary>시간을 흘린 결과 (spec X2, spec-gaps G154). 핸들러 결과는 제출처럼 담긴다 (G148).</summary>
    public sealed class AdvanceResult
    {
        internal AdvanceResult(int advanced, long tick, IReadOnlyList<Reaction> reactions)
        {
            Advanced = advanced;
            Tick = tick;
            Reactions = reactions;
        }

        /// <summary>실제로 진행한 틱 수.</summary>
        public int Advanced { get; }

        /// <summary>진행한 뒤의 틱.</summary>
        public long Tick { get; }

        /// <summary>진행하는 동안 돈 핸들러들, 돈 순서대로.</summary>
        public IReadOnlyList<Reaction> Reactions { get; }

        /// <summary>
        /// 시간을 막는 질문이 열려 있어 멈췄으면 그 질문들, 질문 번호 순 (spec X2, spec-gaps G159). 이때 <see cref="Advanced"/>는 바란 틱보다 작다.
        /// 다 진행했으면 비어 있다. 거른 결과(<see cref="WorldView.Show(AdvanceResult)"/>)에서는 그 연결이 답할 수 있는 것만 있다 (G206).
        /// </summary>
        public IReadOnlyList<Request> BlockedBy
        {
            get => _blockedBy;
            internal set
            {
                _blockedBy = value;
                Blocked = value.Count > 0;
            }
        }

        private IReadOnlyList<Request> _blockedBy = Array.Empty<Request>();

        /// <summary>시간을 막는 질문 때문에 멈췄다. 거른 결과에서도 그대로다(멈춘 것은 모두가 안다, G206).</summary>
        public bool Blocked { get; internal set; }
    }
}
