using System.Collections.Generic;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Loading;
using Kesa.Engine.Schema;
using Kesa.Engine.Script;

namespace Kesa.Engine.Reporting
{
    /// <summary>
    /// 최종 Def 덤프 + 필드별 출처 (spec S9 “왜?” 도구 1, spec-gaps G76). <c>abstract</c>를 포함한 모든 Def를 등록 순서로,
    /// 값마다 최종 값·출처(직접/상속/패치/기본값)·처음 적은 위치·그 칸에 영향을 준 패치를 적용 순서로 낸다.
    /// 구체 Def는 기본값까지 채운 동결본을, <c>abstract</c>는 상속을 펼친 값을 쓴다. 실패한 패치는 없다(G45).
    /// </summary>
    public static class DefDump
    {
        public const int Version = 1;

        public static string ToJson(ContentLoadResult result)
        {
            var defs = new JsonArray();
            foreach (var def in result.Expanded.Defs) defs.Add(new Writer(result).Def(def));
            return new JsonObject().Add("version", JsonNode.Number(Version)).Add("defs", defs).ToText();
        }

        private sealed class Writer
        {
            private readonly ContentLoadResult _result;

            public Writer(ContentLoadResult result) => _result = result;

            public JsonNode Def(ResolvedDef def)
            {

                OrderedMap<ResolvedValue> consts = def.Consts;
                OrderedMap<ResolvedComponent> components = def.Components;
                if (_result.Frozen.TryGet(def.Id, out var frozen))
                {
                    consts = frozen.Consts;
                    components = frozen.Components;
                }

                var constNodes = new JsonObject();
                foreach (var name in consts.Keys)
                {
                    consts.TryGet(name, out var value);
                    constNodes.Add(name, ValueEntry(value));
                }

                var componentNodes = new JsonObject();
                foreach (var name in components.Keys)
                {
                    components.TryGet(name, out var rc);
                    componentNodes.Add(name, Component(rc));
                }

                return new JsonObject()
                    .Add("id", JsonNode.String(def.Id))
                    .Add("type", JsonNode.String(def.Type.Name))
                    .Add("mod", JsonNode.String(def.Mod))
                    .Add("abstract", JsonNode.Bool(def.IsAbstract))
                    .Add("parent", JsonNode.String(def.Record.Parent?.Name))
                    .Add("consts", constNodes)
                    .Add("components", componentNodes);
            }

            private JsonNode Component(ResolvedComponent rc)
            {
                var values = new JsonObject();
                foreach (var member in rc.Members.Keys)
                {
                    rc.Members.TryGet(member, out var value);
                    values.Add(member, ValueEntry(value));
                }
                var rows = new JsonObject();
                foreach (var key in rc.Rows.Keys)
                {
                    rc.Rows.TryGet(key, out var row);
                    var rowValues = new JsonObject();
                    foreach (var member in row.Members.Keys)
                    {
                        row.Members.TryGet(member, out var value);
                        rowValues.Add(member, ValueEntry(value));
                    }
                    rows.Add(key, new JsonObject()
                        .Add("attachedBy", row.AttachedBy == null ? JsonNode.Null : Patch(row.AttachedBy))
                        .Add("values", rowValues));
                }
                return new JsonObject()
                    .Add("attachedBy", rc.AttachedBy == null ? JsonNode.Null : Patch(rc.AttachedBy))
                    .Add("values", values)
                    .Add("rows", rows);
            }

            /// <summary>
            /// 값 한 칸. <c>patches</c>는 이 값을 만든 패치뿐이다(적용 순서, 부모 쪽 먼저): 자식이 직접 적어 덮은 부모 패치는 없다 (G76).
            /// </summary>
            private JsonNode ValueEntry(ResolvedValue value)
            {
                var patches = new JsonArray();
                foreach (var entry in value.Patches) patches.Add(Patch(entry));
                return new JsonObject()
                    .Add("value", Value(value.Value))
                    .Add("origin", JsonNode.String(OriginName(value.Origin)))
                    .Add("source", Source(value))
                    .Add("patches", patches);
            }

            private static string OriginName(ValueOrigin origin)
            {
                switch (origin)
                {
                    case ValueOrigin.Own: return "own";
                    case ValueOrigin.Inherited: return "inherited";
                    case ValueOrigin.Patched: return "patched";
                    default: return "default";
                }
            }

            private static JsonNode Source(ResolvedValue value)
            {
                bool declared = value.Origin == ValueOrigin.Default; // 스키마 선언 위치만 안다
                // 책임 모드와 위치는 오류와 같은 규칙이다 (G64): 패치가 바꾼 값이면 패치 모드, 아니면 처음 적은 모드
                var blame = ValueBlame.Of(value, SourceSpan.None, value.Span);
                var span = declared ? value.Span : blame.Span;
                return new JsonObject()
                    .Add("def", declared ? JsonNode.Null : JsonNode.String(value.Patch?.TargetId ?? value.Source.Id))
                    .Add("mod", declared ? JsonNode.Null : JsonNode.String(blame.Mod))
                    .Add("file", JsonNode.String(span.File))
                    .Add("line", JsonNode.Number(span.Line))
                    .Add("col", JsonNode.Number(span.Col));
            }

            private static JsonNode Patch(PatchLogEntry entry)
            {
                var call = entry.Call;
                return new JsonObject()
                    .Add("mod", JsonNode.String(entry.Mod))
                    .Add("def", JsonNode.String(entry.TargetId))
                    .Add("op", JsonNode.String(entry.Op.ToString().ToLowerInvariant()))
                    .Add("path", JsonNode.String(entry.Path.Text))
                    .Add("value", call.Value == null ? JsonNode.Null : Value(call.Value))
                    .Add("status", JsonNode.String(entry.Status.ToString().ToLowerInvariant()))
                    .Add("file", JsonNode.String(call.Span.File))
                    .Add("line", JsonNode.Number(call.Span.Line))
                    .Add("col", JsonNode.Number(call.Span.Col));
            }

            /// <summary>값 표현 (G76): <c>fixed</c>는 정밀도를 지키려고 문자열, 맵의 키는 모두 문자열.</summary>
            private static JsonNode Value(Value value)
            {
                switch (value)
                {
                    case IntValue i: return JsonNode.Number(i.Value);
                    case FixedValue f: return JsonNode.String(f.Value.ToString());
                    case BoolValue b: return JsonNode.Bool(b.Value);
                    case NoneValue _: return JsonNode.Null;
                    case StrValue s: return JsonNode.String(s.Value);
                    case ListValue list:
                        var array = new JsonArray();
                        foreach (var item in list.Items) array.Add(Value(item));
                        return array;
                    case DictValue dict:
                        var obj = new JsonObject();
                        foreach (var item in dict.Items)
                        {
                            string key = item.Key is StrValue ks ? ks.Value
                                : item.Key is IntValue ki ? ki.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
                                : item.Key.KindName;
                            obj.Add(key, Value(item.Value));
                        }
                        return obj;
                    case FunctionValue fn:
                        return new JsonObject().Add("function", JsonNode.String(fn.Name));
                    case ComponentValue cv:
                        var fields = new JsonObject();
                        foreach (var f in cv.Fields) fields.Add(f.Name, Value(f.Value));
                        return new JsonObject().Add("component", JsonNode.String(cv.Component.Name)).Add("values", fields);
                }
                return JsonNode.String(value.KindName);
            }
        }
    }
}
