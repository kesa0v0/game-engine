using System.Collections.Generic;
using Kesa.Engine.Script;

namespace Kesa.Engine.Runtime
{
    /// <summary>
    /// 도장 뒤에 난 효과 (G426). <see cref="Missed"/>면 커널이 더 들고 있지 않은 효과가 있었고 남은 것만 들어 있다.
    /// </summary>
    public sealed class FxResult
    {
        public FxResult(long stamp, bool missed, IReadOnlyList<FxBatch> batches)
        {
            Stamp = stamp;
            Missed = missed;
            Batches = batches;
        }

        /// <summary>다음에 넘길 도장.</summary>
        public long Stamp { get; }

        public bool Missed { get; }

        /// <summary>커맨드 묶음 순서대로. 효과가 하나도 없는 묶음은 없다.</summary>
        public IReadOnlyList<FxBatch> Batches { get; }
    }

    /// <summary>한 커맨드 묶음(그 뒤 신호 처리까지)에서 난 효과, 신호가 난 순서대로.</summary>
    public sealed class FxBatch
    {
        public FxBatch(long batch, IReadOnlyList<FxItem> effects)
        {
            Batch = batch;
            Effects = effects;
        }

        public long Batch { get; }
        public IReadOnlyList<FxItem> Effects { get; }
    }

    /// <summary>효과 하나. 함수가 실패하면 <see cref="Fx"/>는 없고 <see cref="Error"/>가 그 함수 몫 하나로 실린다.</summary>
    public sealed class FxItem
    {
        public FxItem(string function, FxValue? fx, string? error)
        {
            Function = function;
            Fx = fx;
            Error = error;
        }

        /// <summary>낸 <c>@world_fx</c> 함수 (<c>모드.함수</c>).</summary>
        public string Function { get; }

        public FxValue? Fx { get; }
        public string? Error { get; }
    }
}
