using System;
using System.Collections.Generic;
using System.Numerics;
using Kesa.Engine.Numerics;

namespace Kesa.Engine.Script
{
    /// <summary>
    /// 순수 함수에서 쓰는 내장 함수 (spec-gaps G97·G189): <c>floor ceil round abs min max clamp len range sum any all</c>.
    /// 호스트는 여기에 <c>contribs</c> 같은 것을 더해 <see cref="PureEvaluator"/>에 넘긴다.
    /// </summary>
    public static class PureBuiltins
    {
        /// <summary>내장 함수가 한 번에 펼칠 수 있는 범위의 크기 (반복 문장은 단계 예산으로 막는다).</summary>
        private const long MaxMaterializedRange = 100_000;

        /// <summary>기여 행을 읽는 내장 함수 (G98). 월드가 있어야 하므로 런타임(<c>World</c>)이 이 이름으로 더한다.</summary>
        public const string ContribsName = "contribs";

        /// <summary>지금 틱을 읽는 내장 (spec X2, spec-gaps G154·G155). 월드가 있어야 하므로 런타임이 더한다.</summary>
        public const string NowName = "now";

        /// <summary>Def 타입·컴포넌트로 엔티티 목록을 읽는 전역 순회 (spec S3, spec-gaps G192·G193). 런타임이 더한다.</summary>
        public const string QueryName = "query";

        /// <summary>엔티티에 앉은 연결의 종류를 읽는 내장 (spec S6, spec-gaps G241): <c>"human"</c>·<c>"ai"</c>·<c>None</c>. 런타임이 더한다.</summary>
        public const string ControllerName = "controller";

        /// <summary>
        /// 짧은 Def id를 쓴 코드의 모드 기준 전체 이름으로 바꾸는 내장 (spec-gaps G261·G266). 없는 Def는 오류다. 부른 모드를 알아야 하므로
        /// 런타임이 더한다.
        /// </summary>
        public const string DefIdName = "defid";

        /// <summary>월드의 구조를 바꾸는 내장 (spec-gaps G134, int 키 줄 헬퍼는 G129). 몸체는 월드가 준다.</summary>
        public const string SpawnName = "spawn";
        public const string DestroyName = "destroy";
        public const string AttachName = "attach";
        public const string DetachName = "detach";
        public const string AddRowName = "add_row";
        public const string RemoveRowName = "remove_row";
        public const string ReplaceRowsName = "replace_rows";
        public const string AppendRowName = "append_row";

        /// <summary>멈춰서 답을 기다리는 내장 (spec X1, spec-gaps G138). 자리 규칙은 쓰는 내장과 같다 (G136).</summary>
        public const string AskName = "ask";

        /// <summary>틱이 지날 때까지 멈추는 내장 (spec X2·S5 "자는 Flow"). 자리 규칙은 <c>ask</c>와 같다 (G136).</summary>
        public const string WaitName = "wait";

        /// <summary>시간을 흘리는 내장 (spec X2, spec-gaps G167). <c>wait</c>처럼 멈추고, 엔진이 틱을 진행한 뒤 이어 간다.</summary>
        public const string AdvanceName = "advance";

        /// <summary>신호를 내는 내장 (spec S5). 받은 핸들러가 상태를 바꾸므로 자리 규칙은 쓰는 내장과 같다 (spec-gaps G146).</summary>
        public const string EmitName = "emit";

        /// <summary>
        /// 본문 중간의 비용 <c>ok = pay(칸, 금액)</c> (spec S2, spec-gaps G187). 첫 자리는 <c>cost</c>처럼 칸 자체라(G186) 실행기가
        /// 호출 모양을 직접 본다. 상태를 바꾸므로 자리 규칙은 쓰는 내장과 같다.
        /// </summary>
        public const string PayName = "pay";

        /// <summary>난수 이름 공간 (spec-gaps G179). 몸체는 월드가 준다. 이 이름을 쓰는 함수는 난수 함수다 (G178).</summary>
        public const string RandomName = "random";

        /// <summary>
        /// UI 로컬 기록 <c>ui.Selection.unit</c> (spec-gaps G277·G279). 몸체는 호스트가 부를 때 넘기는 <c>UiState</c>다. 게임 쪽 코드는
        /// 쓸 수 없다 (G282).
        /// </summary>
        public const string UiName = "ui";

        /// <summary>
        /// <c>@ui_action</c>에서 커맨드를 내 달라고 적어 두는 내장 (spec-gaps G278). UI 로컬 쓰기와 자리 규칙이 같다 (G282).
        /// </summary>
        public const string SubmitName = "submit";

        /// <summary>
        /// 월드를 바꾸거나 멈추거나 신호를 내는 내장. 부르면 쓰기다 (G135·G136·G146). 이름 집합보다 먼저 초기화되어야 한다.
        /// </summary>
        private static readonly string[] WritingNameArray =
        {
            SpawnName, DestroyName, AttachName, DetachName, AddRowName, RemoveRowName, ReplaceRowsName, AppendRowName,
            AskName, WaitName, AdvanceName, EmitName, PayName,
        };

        public static IReadOnlyList<string> WritingNames => WritingNameArray;

        public static bool IsWritingName(string name) => Array.IndexOf(WritingNameArray, name) >= 0;

        /// <summary>오류 메시지에서 쓰는 내장이 하는 일: "changes the world", "waits for an answer", "sends a signal".</summary>
        public static string EffectPhrase(string name) =>
            name == AskName ? "waits for an answer" : name == WaitName ? "waits for time to pass" :
            name == AdvanceName ? "makes time pass" :
            name == EmitName ? "sends a signal" : name == PayName ? "takes a cost out of a field" : "changes the world";

        /// <summary>
        /// 실행 중 함수 본문에서 보이는 내장 이름 전체: <see cref="Create"/>의 이름, <see cref="ContribsName"/>, 쓰는 내장.
        /// 로드 시점 이름 검사(G108)가 실행기와 같은 목록을 쓰도록 여기 한 곳에 둔다.
        /// </summary>
        public static IReadOnlyList<string> RuntimeNames => new List<string>(RuntimeNameSet);

        private static readonly SortedSet<string> RuntimeNameSet = NameSet();

        private static SortedSet<string> NameSet()
        {
            var set = new SortedSet<string>(Create().Keys, StringComparer.Ordinal) { ContribsName, NowName, QueryName, ControllerName, RandomName, DefIdName, UiName, SubmitName };
            set.UnionWith(WritingNameArray);
            return set;
        }

        public static bool IsRuntimeName(string name) => RuntimeNameSet.Contains(name);

        public static SortedDictionary<string, Value> Create()
        {
            var table = new SortedDictionary<string, Value>(StringComparer.Ordinal);
            void Add(string name, Func<IReadOnlyList<Value>, Value> body) => table.Add(name, new PureBuiltin(name, body));

            Add("floor", a => Rounding("floor", a, x => FixedMath.Floor(x)));
            Add("ceil", a => Rounding("ceil", a, x => FixedMath.Ceiling(x)));
            Add("round", a => Rounding("round", a, x => FixedMath.Round(x)));
            Add("abs", Abs);
            Add("min", a => Extreme("min", a, -1));
            Add("max", a => Extreme("max", a, 1));
            Add("clamp", Clamp);
            Add("len", Len);
            Add("range", Range);
            Add("sum", Sum);
            Add(AnyName, a => Truth(AnyName, a, stopOn: true));
            Add(AllName, a => Truth(AllName, a, stopOn: false));
            table.Add(TextName, new PureBuiltin(TextName, Text));
            table.Add(OptionName, new PureBuiltin(OptionName, Option));
            return table;
        }

        /// <summary>
        /// 제너레이터 식을 받으면 하나씩 당겨 일찍 멈추는 내장 (spec-gaps G189). 실행기가 그 호출을 알아본다.
        /// </summary>
        public const string AnyName = "any";
        public const string AllName = "all";

        /// <summary>
        /// <c>any(xs)</c>·<c>all(xs)</c> (파이썬과 같다, G189): 참/거짓은 G104. 리스트·<c>range</c>·맵의 키·런타임 묶음을 받는다.
        /// </summary>
        private static Value Truth(string name, IReadOnlyList<Value> args, bool stopOn)
        {
            Arity(name, args, 1);
            foreach (var item in Items(name, args[0]))
                if (PureOps.Truthy(item) == stopOn) return stopOn ? BoolValue.True : BoolValue.False;
            return stopOn ? BoolValue.False : BoolValue.True;
        }

        /// <summary><c>for</c>가 도는 것과 같은 항목: 리스트, 맵의 키, <c>range</c>, 런타임 묶음.</summary>
        private static IEnumerable<Value> Items(string name, Value iterable)
        {
            switch (iterable)
            {
                case ListValue list:
                    foreach (var item in list.Items) yield return item;
                    yield break;
                case DictValue dict:
                    foreach (var item in dict.Items) yield return item.Key;
                    yield break;
                case RangeValue range:
                    for (long i = 0; i < range.Count; i++) yield return new IntValue(range.At(i));
                    yield break;
                case IRuntimeIterable runtime:
                    foreach (var item in runtime.Iterate()) yield return item;
                    yield break;
            }
            throw PureOps.TypeError(name + "() needs a list, not " + iterable.KindName);
        }

        /// <summary>메시지 값을 만드는 내장 (spec-gaps G113).</summary>
        public const string TextName = "text";

        /// <summary><c>text("키", 이름=값, ...)</c>: 위치 인자는 키 하나, 나머지는 이름 있는 인자다.</summary>
        private static Value Text(IReadOnlyList<Value> args, IReadOnlyDictionary<string, Value> keywords) => MakeText(args, keywords, null);

        /// <summary>
        /// <c>text()</c>의 몸체 (spec-gaps G267): 인자는 수·bool·None·문자열·<c>text(...)</c>이고, 그 밖의 값은 <paramref name="runtimeArg"/>가
        /// 판정한다(월드: 엔티티는 그 Def id, 숨김 값은 빈 문자열, 받지 않으면 null). 리스트·dict 등은 번역문에 어떻게 보일지 정할 수 없어 오류다.
        /// </summary>
        internal static TextValue MakeText(IReadOnlyList<Value> args, IReadOnlyDictionary<string, Value> keywords,
            Func<Value, string?>? runtimeArg)
        {
            Arity(TextName, args, 1);
            if (!(args[0] is StrValue key))
                throw PureOps.TypeError("text() takes the text key as a str, not " + args[0].KindName);
            var copy = new SortedDictionary<string, Value>(StringComparer.Ordinal);
            SortedDictionary<string, string>? defs = null;
            foreach (var pair in keywords)
            {
                var v = pair.Value;
                if (!(v is NoneValue || v is BoolValue || v is IntValue || v is FixedValue || v is StrValue || v is TextValue))
                {
                    string? def = runtimeArg?.Invoke(v);
                    if (def == null)
                    {
                        throw PureOps.TypeError("text() cannot show " + pair.Key + "=, which is a " + v.KindName,
                            v is ListValue || v is DictValue
                                ? "pass each value as its own argument, e.g. text(\"k\", first=xs[0], count=len(xs)) (spec-gaps G267)"
                                : "pass numbers, str, bool, None, entities or text(...) (spec-gaps G267)");
                    }
                    if (def.Length > 0) (defs ??= new SortedDictionary<string, string>(StringComparer.Ordinal))[pair.Key] = def;
                }
                copy[pair.Key] = v;
            }
            return new TextValue(key.Value, copy, defs);
        }

        /// <summary>질문의 선택지에 보이는 모양을 싣는 내장 (spec-gaps G271).</summary>
        public const string OptionName = "option";

        private static readonly string[] OptionFields = { "label", "desc", "style", "data" };

        /// <summary>
        /// <c>option(값, label=, desc=, style=, data=)</c>: <c>label</c>·<c>desc</c>는 텍스트 키나 <c>text(...)</c>, <c>style</c>은 문자열,
        /// <c>data</c>는 dict. 어느 것이든 None이면 적지 않은 것과 같다. 저장할 수 있는 값인지는 <c>ask</c>가 본다.
        /// </summary>
        private static Value Option(IReadOnlyList<Value> args, IReadOnlyDictionary<string, Value> keywords)
        {
            Arity(OptionName, args, 1);
            if (args[0] is OptionValue)
                throw PureOps.TypeError("option() takes the value to choose, not another option");
            Value? label = null, desc = null, data = null;
            string? style = null;
            foreach (var pair in keywords)
            {
                var v = pair.Value is NoneValue ? null : pair.Value;
                switch (pair.Key)
                {
                    case "label":
                        label = Shown("label", v);
                        break;
                    case "desc":
                        desc = Shown("desc", v);
                        break;
                    case "style":
                        if (v != null && !(v is StrValue))
                            throw PureOps.TypeError("style= of option() must be a str, not " + v.KindName);
                        style = (v as StrValue)?.Value;
                        break;
                    case "data":
                        if (v != null && !(v is DictValue))
                            throw PureOps.TypeError("data= of option() must be a dict, not " + v.KindName);
                        data = v;
                        break;
                    default:
                        throw new ScriptRuntimeException(Diagnostics.Codes.RunScript, "option() has no argument '" + pair.Key + "'",
                            Diagnostics.Suggest.Hint(pair.Key, OptionFields));
                }
            }
            return new OptionValue(args[0], label, desc, style, data);
        }

        private static Value? Shown(string field, Value? v)
        {
            if (v == null || v is StrValue || v is TextValue) return v;
            throw PureOps.TypeError(field + "= of option() must be a text key (str) or text(...), not " + v.KindName);
        }

        private static void Arity(string name, IReadOnlyList<Value> args, int count)
        {
            if (args.Count != count)
            {
                throw PureOps.TypeError(name + "() takes " + count + " argument" + (count == 1 ? "" : "s") + ", got " + args.Count);
            }
        }

        private static Value NumberArg(string name, Value v)
        {
            if (PureOps.IsNumber(v)) return v;
            throw PureOps.TypeError(name + "() needs a number, not " + v.KindName);
        }

        private static Value Rounding(string name, IReadOnlyList<Value> args, Func<Fixed, long> round)
        {
            Arity(name, args, 1);
            var v = NumberArg(name, args[0]);
            return v is IntValue ? v : new IntValue(round(((FixedValue)v).Value));
        }

        private static Value Abs(IReadOnlyList<Value> args)
        {
            Arity("abs", args, 1);
            var v = NumberArg("abs", args[0]);
            if (PureOps.CompareNumbers(v, new IntValue(0)) >= 0) return v;
            return PureOps.Negate(v);
        }

        /// <summary>인자 하나가 리스트면 그 원소, 아니면 인자들 자체가 대상이다.</summary>
        private static IReadOnlyList<Value> Operands(string name, IReadOnlyList<Value> args)
        {
            if (args.Count == 1)
            {
                switch (args[0])
                {
                    case ListValue list: return list.Items;
                    case RangeValue range:
                        if (range.Count > MaxMaterializedRange)
                        {
                            throw PureOps.TypeError(name + "() over a range of " + range.Count + " elements is too long",
                                "loop over the range instead");
                        }
                        var items = new List<Value>();
                        for (long i = 0; i < range.Count; i++) items.Add(new IntValue(range.At(i)));
                        return items;
                    default:
                        throw PureOps.TypeError(name + "() with one argument needs a list, not " + args[0].KindName,
                            PureOps.ListHint(args[0]));
                }
            }
            if (args.Count == 0) throw PureOps.TypeError(name + "() needs at least one argument");
            return args;
        }

        /// <summary>하나라도 <c>fixed</c>가 있으면 결과도 <c>fixed</c>다 (G95의 승격).</summary>
        private static Value Promote(Value v, bool anyFixed) =>
            anyFixed && v is IntValue ? new FixedValue(PureOps.ToFixed(v)) : v;

        private static Value Extreme(string name, IReadOnlyList<Value> args, int direction)
        {
            var items = Operands(name, args);
            if (items.Count == 0) throw PureOps.TypeError(name + "() of an empty list");
            Value best = NumberArg(name, items[0]);
            bool anyFixed = best is FixedValue;
            for (int i = 1; i < items.Count; i++)
            {
                var v = NumberArg(name, items[i]);
                anyFixed |= v is FixedValue;
                if (PureOps.CompareNumbers(v, best) * direction > 0) best = v;
            }
            return Promote(best, anyFixed);
        }

        private static Value Clamp(IReadOnlyList<Value> args)
        {
            Arity("clamp", args, 3);
            var x = NumberArg("clamp", args[0]);
            var lo = NumberArg("clamp", args[1]);
            var hi = NumberArg("clamp", args[2]);
            if (PureOps.CompareNumbers(lo, hi) > 0)
                throw PureOps.TypeError("clamp() lower bound " + Show(lo) + " is greater than the upper bound " + Show(hi));
            bool anyFixed = x is FixedValue || lo is FixedValue || hi is FixedValue;
            Value result = PureOps.CompareNumbers(x, lo) < 0 ? lo : PureOps.CompareNumbers(x, hi) > 0 ? hi : x;
            return Promote(result, anyFixed);
        }

        private static string Show(Value v) =>
            v is IntValue i ? i.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : ((FixedValue)v).Value.ToString();

        private static Value Len(IReadOnlyList<Value> args)
        {
            Arity("len", args, 1);
            switch (args[0])
            {
                case ListValue list: return new IntValue(list.Items.Count);
                case DictValue dict: return new IntValue(dict.Items.Count);
                case RangeValue range: return new IntValue(range.Count);
                case IRuntimeCollection collection: return new IntValue(collection.Size());
            }
            throw PureOps.TypeError("len() needs a list, dict, range or component rows, not " + args[0].KindName);
        }

        private static Value Range(IReadOnlyList<Value> args)
        {
            if (args.Count < 1 || args.Count > 3)
                throw PureOps.TypeError("range() takes 1 to 3 arguments, got " + args.Count);
            var ints = new long[args.Count];
            for (int i = 0; i < args.Count; i++)
            {
                if (!(args[i] is IntValue n)) throw PureOps.TypeError("range() needs int arguments, not " + args[i].KindName);
                ints[i] = n.Value;
            }

            long start = args.Count == 1 ? 0 : ints[0];
            long stop = args.Count == 1 ? ints[0] : ints[1];
            long step = args.Count == 3 ? ints[2] : 1;
            if (step == 0) throw PureOps.TypeError("range() step must not be zero");

            // 원소 수 = ceil((stop - start) / step), 범위를 넘지 않게 큰 정수로 센다
            var span = new BigInteger(stop) - new BigInteger(start);
            BigInteger count = BigInteger.Zero;
            if (step > 0 && span > 0) count = (span + step - 1) / step;
            else if (step < 0 && span < 0) count = (span + step + 1) / step;
            if (count > long.MaxValue) throw PureOps.ArithmeticError("range() is too long");
            return new RangeValue(start, step, (long)count);
        }

        /// <summary>파이썬과 같은 <c>sum(묶음, start=0)</c>: 첫 인자는 리스트나 range, 둘째는 시작값(수)이다.</summary>
        private static Value Sum(IReadOnlyList<Value> args)
        {
            if (args.Count < 1 || args.Count > 2)
                throw PureOps.TypeError("sum() takes a list and an optional start value, got " + args.Count + " arguments");
            if (!(args[0] is ListValue) && !(args[0] is RangeValue))
                throw PureOps.TypeError("sum() needs a list or range, not " + args[0].KindName,
                    "write sum([a, b]) to add separate values");
            var items = Operands("sum", new[] { args[0] });
            Value total = args.Count == 2 ? NumberArg("sum", args[1]) : new IntValue(0);
            foreach (var item in items) total = PureOps.Arithmetic(BinaryOp.Add, total, NumberArg("sum", item));
            return total;
        }
    }
}
