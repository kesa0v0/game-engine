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
        /// <summary>
        /// 낸 신호의 대기열(발생 순서). 커맨드나 핸들러가 실패하면 되감기로 빠진다(C8). 제출·답·응답이 끝날 때 비운다(G148).
        /// </summary>
        private readonly List<Emitted> _emitted = new List<Emitted>();

        /// <summary>아직 핸들러로 처리하지 않은 신호 수 (테스트·진단용).</summary>
        public int PendingSignalCount => _emitted.Count;

        private void AddSignalBuiltins(SortedDictionary<string, Value> table)
        {
            // emit(Damaged(...)): 쓰는 내장이라 쓰기 모드 프레임의 문장으로만 불린다 (G146)
            table.Add(PureBuiltins.EmitName,
                new PureBuiltin(PureBuiltins.EmitName, (p, k) => Guard(() => Emit(p, k))) { Writes = true });
        }

        /// <summary>신호 값 생성자: 컴포넌트 생성자와 같은 스키마 이름 칸이다 (G230).</summary>
        private void AddSignalConstructors(SortedDictionary<string, KeyValuePair<string, Value>> names)
        {
            foreach (var signal in _schema.Signals)
            {
                var s = signal;
                var constructor = EngineSignals.IsEngine(s.Name)
                    // 엔진 신호의 값은 엔진만 만든다. 이름이 적힌 호출은 로드 때 잡히고, 함수 값을 거친 길은 여기서 막는다 (G156)
                    ? new PureBuiltin(s.Name, (p, k) => throw new ScriptRuntimeException(Codes.RunScript, EngineSignals.Why(s.Name)))
                    : new PureBuiltin(s.Name, (p, k) => ConstructSignal(s, p, k));
                constructor.Kind = "signal constructor " + s.Name;
                // 컴포넌트와 신호는 이름을 함께 쓴다 (스키마의 이름 겹침 검사)
                if (!names.ContainsKey(s.Name)) names.Add(s.Name, new KeyValuePair<string, Value>(s.Mod, constructor));
            }
        }

        private Value Emit(IReadOnlyList<Value> positional, IReadOnlyDictionary<string, Value> keywords)
        {
            const string usage = "emit(Signal(name=value, ...))";
            if (positional.Count != 1 || keywords.Count > 0)
                throw new ScriptRuntimeException(Codes.RunScript,
                    "emit() takes one signal value, got " + positional.Count + " positional and " + keywords.Count + " named arguments",
                    "write " + usage);
            if (!(positional[0] is SignalValue signal))
                throw new ScriptRuntimeException(Codes.RunType,
                    "emit() takes a signal value, not " + positional[0].KindName, "write " + usage + " (spec-gaps G145)");
            int at = _emitted.Count;
            // 핸들러가 낸 신호는 그 핸들러가 받은 신호보다 한 세대 깊다 (G147)
            _emitted.Add(new Emitted(signal, _reacting == null ? 1 : _reacting.Generation + 1, _reactingHandler, _reacting));
            Journal(() => _emitted.RemoveAt(at));
            return NoneValue.Instance;
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

            /// <summary>이름으로 저장 가능한 값 하나 (선언에 있는 이름만).</summary>
            public Value Value(string name)
            {
                foreach (var pair in Values)
                    if (pair.Key.Name == name) return pair.Value;
                throw new InvalidOperationException("engine: signal " + Signal.Name + " has no value '" + name + "'");
            }

            /// <summary>결과에 담을 값들 (이름 순).</summary>
            public IReadOnlyDictionary<string, Value> Plain()
            {
                var map = new SortedDictionary<string, Value>(StringComparer.Ordinal);
                foreach (var pair in Values) map[pair.Key.Name] = pair.Value;
                return map;
            }

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
