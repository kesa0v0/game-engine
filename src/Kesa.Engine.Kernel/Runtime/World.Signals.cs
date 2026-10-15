using System;
using System.Collections.Generic;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Schema;
using Kesa.Engine.Script;

namespace Kesa.Engine.Runtime
{
    /// <summary>
    /// 신호 값 (spec S5, spec-gaps G145): <c>Damaged(target=u, amount=3)</c>. 선언한 인자를 모두 받고 타입을 커맨드 인자처럼(G115)
    /// 맞춘다. 값은 저장 가능한 모양으로 들고 있어 멈춘 Flow에 남아도 저장된다(G141).
    /// </summary>
    public sealed partial class World
    {
        private void AddSignalBuiltins(SortedDictionary<string, Value> table)
        {
            // 이름 검사(G108)가 그 모드에 보이는 신호만 통과시킨다
            foreach (var signal in _schema.Signals)
            {
                var s = signal;
                if (table.ContainsKey(s.Name)) continue; // 내장과 같은 이름의 신호는 생성자로 쓸 수 없다
                table.Add(s.Name, new PureBuiltin(s.Name, (p, k) => ConstructSignal(s, p, k)));
            }
        }

        private Value ConstructSignal(SignalType signal, IReadOnlyList<Value> positional, IReadOnlyDictionary<string, Value> keywords)
        {
            if (positional.Count > 0)
                throw new ScriptRuntimeException(Codes.RunScript,
                    signal.Name + "(...) takes values by name, like " + signal.Name + "(" + Usage(signal) + ")");
            var values = SignalValues(signal, keywords, out var problems);
            if (problems.Count > 0)
                throw new ScriptRuntimeException(problems[0].Problem == ArgumentProblem.WrongType ? Codes.RunType : Codes.RunScript,
                    "making the signal " + signal.Name + ": " + problems[0].Message,
                    "a signal takes all of its values: " + signal.Name + "(" + Usage(signal) + ") (spec-gaps G145)");
            return new SignalValue(this, signal, values);
        }

        /// <summary>
        /// 신호의 인자를 선언과 맞춘다(선언 순서). 빠진 것·없는 이름·타입이 다른 것은 <paramref name="problems"/>에 모은다.
        /// </summary>
        private List<KeyValuePair<SignalField, Value>> SignalValues(SignalType signal, IReadOnlyDictionary<string, Value> given,
            out List<ArgumentRejection> problems)
        {
            problems = new List<ArgumentRejection>();
            var values = new List<KeyValuePair<SignalField, Value>>();
            foreach (var field in signal.Fields)
            {
                if (!given.TryGetValue(field.Name, out var value))
                {
                    problems.Add(new ArgumentRejection(field.Name, field.Name, ArgumentProblem.Missing, field.Type.Display,
                        "'" + field.Name + "' (" + field.Type.Display + ") is missing"));
                    continue;
                }
                var fitted = Fit(field.Name, field.Name, field.Type, ToPlain(signal.Name + "." + field.Name, value), problems);
                if (fitted != null) values.Add(new KeyValuePair<SignalField, Value>(field, fitted));
            }
            var extra = new List<string>();
            foreach (var pair in given)
                if (!signal.TryGetField(pair.Key, out _)) extra.Add(pair.Key);
            extra.Sort(StringComparer.Ordinal);
            foreach (var name in extra)
            {
                var names = new List<string>();
                foreach (var f in signal.Fields) names.Add(f.Name);
                string? hint = Suggest.Hint(name, names);
                problems.Add(new ArgumentRejection(name, name, ArgumentProblem.Unexpected, null,
                    "signal " + signal.Name + " has no value '" + name + "'" + (hint == null ? "" : "; " + hint)));
            }
            return values;
        }

        private static string Usage(SignalType signal)
        {
            var parts = new List<string>();
            foreach (var f in signal.Fields) parts.Add(f.Name + "=...");
            return string.Join(", ", parts);
        }

        /// <summary><c>Damaged(target=u, amount=3)</c>. <c>d.amount</c>로 읽는다.</summary>
        private sealed class SignalValue : Value, IRuntimeAttributes, IValueEquality
        {
            private readonly World _world;

            public SignalValue(World world, SignalType signal, IReadOnlyList<KeyValuePair<SignalField, Value>> values)
            {
                _world = world;
                Signal = signal;
                Values = values;
            }

            public SignalType Signal { get; }

            /// <summary>선언 순서의 저장 가능한 값.</summary>
            public IReadOnlyList<KeyValuePair<SignalField, Value>> Values { get; }

            public override string KindName => Signal.Name + "(...)";

            /// <summary>같은 신호에 같은 값들이면 같다 (G142).</summary>
            public bool ValueEquals(Value other)
            {
                if (!(other is SignalValue s) || s.Signal != Signal || s.Values.Count != Values.Count) return false;
                for (int i = 0; i < Values.Count; i++)
                    if (s.Values[i].Key != Values[i].Key || !PureOps.ValuesEqual(s.Values[i].Value, Values[i].Value)) return false;
                return true;
            }

            public Value GetAttribute(string name)
            {
                foreach (var pair in Values)
                    if (pair.Key.Name == name) return _world.ToScript(pair.Value);
                var names = new List<string>();
                foreach (var f in Signal.Fields) names.Add(f.Name);
                throw new ScriptRuntimeException(Codes.RunScript, "signal " + Signal.Name + " has no value '" + name + "'",
                    Suggest.Hint(name, names));
            }
        }
    }
}
