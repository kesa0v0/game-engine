using System.Collections.Generic;
using Kesa.Engine.Script;

namespace Kesa.Engine.Runtime
{
    /// <summary>
    /// 감싼 함수 (spec-gaps G311~G314). 대상 함수를 부르는 모든 자리가 맨 바깥 감싸기를 부르고, 감싸기는 첫 인자로 받은 안쪽 함수를
    /// 부른다. 순서는 로드 순서 → 정의 순서로 뒤의 것이 바깥이다. 게임이 돌 때만 적용된다(로드 중의 계산은 원래 함수, G313).
    /// </summary>
    public sealed partial class World
    {
        /// <summary>대상 함수의 정의 → 감싼 함수들, 안쪽부터.</summary>
        private readonly Dictionary<FunctionDef, List<FunctionValue>> _wraps = new Dictionary<FunctionDef, List<FunctionValue>>();

        /// <summary>감싸는 함수의 정의 → (대상, 안쪽부터 몇째인가). 불러온 프레임의 안쪽 함수를 다시 만든다 (G314).</summary>
        private readonly Dictionary<FunctionDef, KeyValuePair<FunctionValue, int>> _wrapperOf =
            new Dictionary<FunctionDef, KeyValuePair<FunctionValue, int>>();

        private void SetWraps(IReadOnlyList<WrapDecl> wraps)
        {
            foreach (var w in wraps)
            {
                var target = w.Target!;
                if (!_wraps.TryGetValue(target.Definition!, out var chain)) _wraps.Add(target.Definition!, chain = new List<FunctionValue>());
                _wrapperOf[w.Function.Definition!] = new KeyValuePair<FunctionValue, int>(target, chain.Count);
                chain.Add(w.Function);
            }
        }

        private IReadOnlyList<FunctionValue>? WrappersOf(FunctionValue fn) =>
            _wraps.TryGetValue(fn.Definition!, out var chain) ? chain : null;

        /// <summary>
        /// 불러온 감싸기 프레임의 첫 매개변수(안쪽 함수)를 지금의 감싸기 순서로 채운다 (G314). 감싸기 프레임이 아니면 그대로 둔다.
        /// </summary>
        private void RestoreInner(Frame frame)
        {
            var def = frame.Function.Definition;
            if (def == null || !_wrapperOf.TryGetValue(def, out var place)) return;
            frame.Locals[def.Parameters[0].Name] = new InnerFunction(place.Key, place.Value - 1);
        }
    }
}
