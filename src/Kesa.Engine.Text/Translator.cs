using System;
using System.Collections.Generic;
using System.Globalization;
using Kesa.Engine.Runtime;
using Kesa.Engine.Script;
using Linguini.Bundle;
using Linguini.Shared.Types.Bundle;

namespace Kesa.Engine.Text
{
    /// <summary>
    /// 한 플레이어의 언어 목록에 맞춘 번역기 (spec-gaps G269·G270). 결과는 늘 문자열이고 예외를 던지지 않는다.
    /// 폴백은 언어 목록 → 키의 주인의 기본 언어 → 키 자체 (G257). 번역 오류는 <c>onError</c>로 받는다.
    /// </summary>
    public sealed class Translator
    {
        private readonly IReadOnlyList<FluentBundle> _chain;
        private readonly Translations _translations;
        private readonly Action<string>? _onError;

        internal Translator(IReadOnlyList<FluentBundle> chain, Translations translations, Action<string>? onError)
        {
            _chain = chain;
            _translations = translations;
            _onError = onError;
        }

        /// <summary>Def의 번역 키: <c>harbor:rigs/Brig</c> → <c>def-harbor-rigs-Brig</c> (G262).</summary>
        public static string DefKey(string defId) => "def-" + defId.Replace(':', '-').Replace('/', '-');

        /// <summary>스크립트가 만든 쪽지 (<c>require</c> 메시지·<c>text(...)</c>).</summary>
        public string Format(TextValue text) => Lookup(text.Key, Arguments(text));

        /// <summary>호스트가 키로 직접 (<c>tr.Text("harbor-gold", ("n", new IntValue(5)))</c>).</summary>
        public string Text(string key, params (string Name, Value Value)[] args)
        {
            var map = new Dictionary<string, IFluentType>(StringComparer.Ordinal);
            foreach (var (name, value) in args) map[name] = Argument(value, null);
            return Lookup(key, map);
        }

        /// <summary>
        /// 제목·선택지 자리 (G270): 문자열은 Def의 전체 이름이면 그 Def의 이름, 아니면 키. 쪽지는 번역, 엔티티는 그 Def의 이름,
        /// 숨김 값은 <c>engine-hidden</c>, <c>option</c>은 <c>label</c>이 있으면 그것 (G271). 그 밖은 <see cref="Value(Value, WorldView)"/>와 같다.
        /// </summary>
        public string Label(Value value, WorldView? view = null) => Label(value, DefOf(view));

        public string Label(Value value, World world) => Label(value, DefOf(world));

        /// <summary>
        /// 그 값의 Fluent 속성 (G263·G270): 키면 <c>키.속성</c>, Def id·엔티티면 <c>def-….속성</c>, 쪽지면 그 키의 속성을 같은 인자로.
        /// <c>option</c>에 <c>desc</c>가 있고 속성이 <c>"desc"</c>면 그것을 쓴다.
        /// </summary>
        public string Attr(Value value, string attribute, WorldView? view = null) => Attr(value, attribute, DefOf(view));

        public string Attr(Value value, string attribute, World world) => Attr(value, attribute, DefOf(world));

        /// <summary>화면 값 안의 맨 값 하나 (G267·G269): 문자열은 Def의 전체 이름이 아니면 글자 그대로.</summary>
        public string Value(Value value, WorldView? view = null) => Value(value, DefOf(view));

        public string Value(Value value, World world) => Value(value, DefOf(world));

        // ---- 자리별 규칙 ----

        private string Label(Value value, Func<EntityId, string?> defOf)
        {
            switch (value)
            {
                case StrValue s:
                    return IsDef(s.Value) ? Lookup(DefKey(s.Value), null) : Lookup(s.Value, null);
                case OptionValue o:
                    return o.Label != null ? Label(o.Label, defOf) : Label(o.Value, defOf);
                default:
                    return Value(value, defOf);
            }
        }

        private string Attr(Value value, string attribute, Func<EntityId, string?> defOf)
        {
            switch (value)
            {
                case StrValue s:
                    return Lookup((IsDef(s.Value) ? DefKey(s.Value) : s.Value) + "." + attribute, null);
                case EntityValue e:
                    var def = defOf(e.Id);
                    return def == null ? Hidden() : Lookup(DefKey(def) + "." + attribute, null);
                case TextValue t:
                    return Lookup(t.Key + "." + attribute, Arguments(t));
                case OptionValue o:
                    return attribute == "desc" && o.Desc != null ? Label(o.Desc, defOf) : Attr(o.Value, attribute, defOf);
                case HiddenValue _:
                    return Hidden();
                default:
                    Error("cannot look up ." + attribute + " of " + value.KindName);
                    return "";
            }
        }

        private string Value(Value value, Func<EntityId, string?> defOf)
        {
            switch (value)
            {
                case StrValue s:
                    return IsDef(s.Value) ? Lookup(DefKey(s.Value), null) : s.Value;
                case EntityValue e:
                    var def = defOf(e.Id);
                    return def == null ? Hidden() : Lookup(DefKey(def), null);
                case TextValue t:
                    return Format(t);
                case OptionValue o:
                    return Label(o, defOf);
                case HiddenValue _:
                    return Hidden();
                default:
                    return Plain(value) ?? CannotShow(value);
            }
        }

        // ---- 번역 인자 (G267) ----

        private Dictionary<string, IFluentType> Arguments(TextValue text)
        {
            var map = new Dictionary<string, IFluentType>(StringComparer.Ordinal);
            foreach (var pair in text.Args)
                map[pair.Key] = Argument(pair.Value, text.EntityDefs.TryGetValue(pair.Key, out var def) ? def : null);
            return map;
        }

        /// <param name="entityDef">엔티티 인자면 <c>text()</c> 때 적어 둔 Def (G267)</param>
        private IFluentType Argument(Value value, string? entityDef)
        {
            switch (value)
            {
                case IntValue i:
                    return new ExactNumber(i.Value.ToString(CultureInfo.InvariantCulture));
                case FixedValue f:
                    return new ExactNumber(f.Value.ToString());
                case StrValue s:
                    return IsDef(s.Value) ? Def(s.Value) : new FluentString(s.Value);
                case TextValue t:
                    return new FluentString(Format(t));
                case EntityValue _:
                    return entityDef == null ? new FluentString(Hidden()) : Def(entityDef);
                case HiddenValue _:
                    return new FluentString(Hidden());
                default:
                    return new FluentString(Plain(value) ?? CannotShow(value));
            }
        }

        /// <summary>bool·None은 <c>true</c>·<c>false</c>·<c>none</c>, 수는 십진 표기 (G267).</summary>
        private static string? Plain(Value value) => value switch
        {
            BoolValue b => b.Value ? "true" : "false",
            NoneValue _ => "none",
            IntValue i => i.Value.ToString(CultureInfo.InvariantCulture),
            FixedValue f => f.Value.ToString(),
            _ => null,
        };

        private string CannotShow(Value value)
        {
            Error("cannot show " + value.KindName);
            return "";
        }

        // ---- 찾기 ----

        private bool IsDef(string id) => _translations.Defs.TryGet(id, out _);

        private string Hidden() => Lookup(EngineTexts.Hidden, null);

        /// <summary>번역 인자 자리의 Def: 이름과 함께 Def id를 들고 가 <c>JOSA</c>·<c>DEFATTR</c>가 쓴다 (G267·G268).</summary>
        private DefName Def(string defId) =>
            new DefName(defId, Lookup(DefKey(defId), null), attribute => TryLookup(DefKey(defId) + "." + attribute, null));

        /// <summary><c>키</c>나 <c>키.속성</c>을 언어 목록 → 주인의 기본 언어 순으로 찾고, 없으면 키 그대로 (G257).</summary>
        private string Lookup(string key, IDictionary<string, IFluentType>? args)
        {
            var text = TryLookup(key, args);
            if (text != null) return text;
            Error("no translation for '" + key + "'");
            return key;
        }

        /// <summary>없으면 알리지 않고 null (<c>DEFATTR</c>의 없는 속성은 빈 문자열, G268).</summary>
        private string? TryLookup(string key, IDictionary<string, IFluentType>? args)
        {
            int dot = key.IndexOf('.');
            string id = dot < 0 ? key : key.Substring(0, dot);
            foreach (var bundle in _chain)
                if (Has(bundle, key, dot)) return FormatIn(bundle, key, dot, args);
            var owner = _translations.OwnerBundle(id);
            if (owner != null && Has(owner, key, dot)) return FormatIn(owner, key, dot, args);
            return null;
        }

        private static bool Has(FluentBundle bundle, string key, int dot) =>
            dot < 0 ? bundle.HasMessage(key) : bundle.HasAttrMessage(key);

        private string FormatIn(FluentBundle bundle, string key, int dot, IDictionary<string, IFluentType>? args)
        {
            string? text;
            IList<Linguini.Bundle.Errors.FluentError>? errors;
            // 빠진 인자 같은 오류가 나도 Fluent는 글을 채운다({$n}, select의 기본 가지). 그 글을 쓰고 오류만 알린다
            if (dot < 0) bundle.TryGetMessage(key, args, out errors, out text);
            else bundle.TryGetAttrMessage(key, args, out errors, out text);
            if (errors != null)
                foreach (var e in errors) Error("'" + key + "': " + e);
            if (text == null)
            {
                Error("'" + key + "' has no value");
                return key;
            }
            return text;
        }

        private void Error(string message) => _onError?.Invoke(message);

        private static Func<EntityId, string?> DefOf(WorldView? view) =>
            id => view != null && view.Sees(id) ? view.DefOf(id) : null;

        private static Func<EntityId, string?> DefOf(World world) =>
            id => world.IsAlive(id) ? world.DefOf(id) : null;
    }
}
