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
        /// 커맨드 감싸기면 저장의 <c>inner</c>에서 사전 조건을 마친 안쪽 프레임이나 "이미 부름"을 되살린다 (G321).
        /// 안쪽 함수가 바뀌었으면 문제를 적고 false.
        /// </summary>
        private bool RestoreInner(Frame frame, JsonObject node, string where, LoadProblems problems)
        {
            var def = frame.Function.Definition;
            if (def == null || !_wrapperOf.TryGetValue(def, out var place)) return true;
            var target = place.Key;
            int level = place.Value - 1;
            string slot = def.Parameters[0].Name;
            if (!target.IsCommand)
            {
                frame.Locals[slot] = new InnerFunction(target, level);
                return true;
            }
            var saved = AsObject(Field(node, "inner"), "the inner command");
            if (saved.Get("used") != null)
            {
                frame.Locals[slot] = new InnerFunction(target, level, null, used: true);
                return true;
            }
            var fn = level < 0 ? target : _wraps[target.Definition!][level];
            if (Text(Field(saved, "mod"), "a mod id") != fn.Mod || Text(Field(saved, "function"), "a function name") != fn.Name ||
                Text(Field(saved, "hash"), "a function hash") != BodyHash.Of(fn))
            {
                problems.Add(where + ": the wrappers of the command '" + target.Name + "' changed, so the paused position no longer exists");
                return false;
            }
            var prepared = Evaluator.NewFrame(fn);
            foreach (var pair in AsObject(Field(saved, "locals"), "locals").Members)
            {
                if (!prepared.Locals.TrySet(pair.Key, LoadScriptValue(pair.Value, where, problems)))
                    throw new JsonFormatException(where + ": the function '" + fn.Mod + "." + fn.Name + "' has no local '" + pair.Key + "'");
            }
            if (!RestoreInner(prepared, saved, where, problems)) return false;
            frame.Locals[slot] = new InnerFunction(target, level, prepared);
            return true;
        }
    }
}
