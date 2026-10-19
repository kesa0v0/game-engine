using System;
using System.Collections.Generic;
using System.Globalization;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Script;

namespace Kesa.Engine.Runtime
{
    /// <summary>
    /// 호스트가 모드의 순수 함수를 이름으로 부른다 (spec-gaps G244·G245·G247). UI가 보여 줄 값은 이 함수들이 만들고, 호스트 연결부는
    /// UI 정의에서 읽은 이름으로 부를 뿐 게임 단어를 모른다.
    /// </summary>
    public sealed partial class World
    {
        /// <summary>
        /// <c>모드.함수</c>를 위치 순서의 인자로 부른다. 쓰기 함수·난수 함수·커맨드·핸들러·반응은 부르지 않는다(G245). 없는 이름, 인자 수,
        /// 스크립트 오류, 걸음 예산(G224) 초과는 <see cref="WorldException"/>이다(G247). 아무것도 바꾸지 않는다.
        /// </summary>
        public CallResult Call(string function, params Value[] arguments)
        {
            NoHostWrites("Call");
            var fn = Callable(function, arguments.Length);
            var args = new List<Value>(arguments.Length);
            foreach (var a in arguments) args.Add(ToScript(a));
            return new CallResult(function, RunGateFunction(fn, args, function));
        }

        /// <summary>
        /// 부르기 전에 확인한다: 부를 수 있으면 null, 아니면 이유 문장(없는 이름이면 "혹시 …?"). UI 정의를 읽을 때 칸마다 쓴다 (G245·G247).
        /// </summary>
        public string? CheckCall(string function, int argumentCount)
        {
            try
            {
                Callable(function, argumentCount);
                return null;
            }
            catch (WorldException e)
            {
                return e.Hint == null ? e.Message : e.Message + " (" + e.Hint + ")";
            }
        }

        private FunctionValue Callable(string function, int argumentCount)
        {
            int dot = function.LastIndexOf('.');
            if (dot <= 0 || dot == function.Length - 1)
                throw new WorldException(Codes.RunUnknownName,
                    "'" + function + "' is not a function name; write it as mod.function", "for example skirmish.unit_row");
            string mod = function.Substring(0, dot);
            string name = function.Substring(dot + 1);
            if (!_scopes.TryGetValue(mod, out var scope))
                throw Unknown("there is no mod '" + mod + "'", mod, _scopes.Keys);
            bool found = scope.TryGet(name, out var bound, out _);
            if (!(bound is FunctionValue fn))
            {
                var names = new List<string>();
                foreach (var n in scope.Names)
                    if (scope.TryGet(n, out var v, out _) && v is FunctionValue f && Refusal(f) == null) names.Add(n);
                throw Unknown("mod '" + mod + "' has no function '" + name + "'" +
                              (found ? " ('" + name + "' is a " + bound.KindName + ")" : ""), name, names);
            }
            string? refusal = Refusal(fn);
            if (refusal != null)
                throw new WorldException(Codes.RunNotCallable, "'" + function + "' cannot be called by the host: " + refusal,
                    "the host calls pure functions only; move the reading part into its own function (spec-gaps G245)");
            int count = fn.Parameters.Count;
            if (argumentCount != count)
                throw new WorldException(Codes.RunNotCallable,
                    "'" + function + "' takes " + Count(count) + ", not " + argumentCount.ToString(CultureInfo.InvariantCulture),
                    "pass the arguments in the order the function lists them (spec-gaps G247)");
            return fn;
        }

        /// <summary>호스트가 부르지 않는 함수면 그 이유, 아니면 null (G245).</summary>
        private static string? Refusal(FunctionValue fn)
        {
            if (fn.IsEntry) return "it is a " + fn.EntryKind + ", which runs only when submitted or triggered";
            if (fn.Writes) return "it changes the world (at " + fn.WriteWitness!.Value + ")";
            if (fn.Random) return "it uses random numbers (at " + fn.RandomWitness!.Value + ")";
            return null;
        }

        private static string Count(int n) =>
            n == 1 ? "1 argument" : n.ToString(CultureInfo.InvariantCulture) + " arguments";
    }
}
