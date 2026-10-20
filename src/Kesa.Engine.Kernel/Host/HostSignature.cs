using System.Collections.Generic;
using Kesa.Engine.Schema;
using Kesa.Engine.Script;

namespace Kesa.Engine.Host
{
    /// <summary>로드한 스키마로 푼 호스트 함수의 서명 (spec S7, spec-gaps G215).</summary>
    public sealed class HostSignature
    {
        internal HostSignature(HostFunction function, IReadOnlyList<CommandParameter> parameters, TypeRef? returns)
        {
            Function = function;
            Parameters = parameters;
            Returns = returns;
        }

        public HostFunction Function { get; }

        /// <summary>매개변수와 그 타입·기본값. 커맨드 매개변수와 같은 모양이다.</summary>
        public IReadOnlyList<CommandParameter> Parameters { get; }

        /// <summary>반환 타입. <c>-> None</c>이면 null.</summary>
        public TypeRef? Returns { get; }

        /// <summary>
        /// 풀린 서명의 정규 글: <c>(start: Ref["Tile"], max_steps: int = 50) -> list[Ref["Tile"]]</c>. 공백·따옴표 같은 적은 모양이 아니라
        /// 뜻이 같으면 같다. 저장에 싣고 불러올 때 비교한다 (G221).
        /// </summary>
        public string Canonical
        {
            get
            {
                var parts = new List<string>();
                foreach (var p in Parameters)
                    parts.Add(p.Name + ": " + p.Type.Display + (p.Default == null ? "" : " = " + Show(p.Default)));
                return "(" + string.Join(", ", parts) + ") -> " + (Returns == null ? "None" : Returns.Display);
            }
        }

        private static string Show(Value value)
        {
            switch (value)
            {
                case NoneValue _: return "None";
                case BoolValue b: return b.Value ? "True" : "False";
                case IntValue i: return i.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                case FixedValue f: return f.Value.ToString();
                case StrValue s: return Reporting.CheckReport.Quote(s.Value);
                case ListValue list:
                    var items = new List<string>();
                    foreach (var item in list.Items) items.Add(Show(item));
                    return "[" + string.Join(", ", items) + "]";
                case DictValue dict:
                    var entries = new List<string>();
                    foreach (var item in dict.Items) entries.Add(Show(item.Key) + ": " + Show(item.Value));
                    return "{" + string.Join(", ", entries) + "}";
            }
            return value.KindName;
        }
    }
}
