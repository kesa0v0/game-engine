using System;
using System.Collections.Generic;
using System.Globalization;
using Kesa.Engine.Bridge;
using Kesa.Engine.Bridge.Local;
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
        public string Format(BridgeText text) => Lookup(text.Key, Arguments(text));

        public string Format(TextValue text) => Value(text, NoEntities);

        /// <summary>호스트가 키로 직접 (<c>tr.Text("harbor-gold", ("n", new BridgeInt(5)))</c>).</summary>
        public string Text(string key, params (string Name, BridgeValue Value)[] args)
        {
            var map = new Dictionary<string, IFluentType>(StringComparer.Ordinal);
            foreach (var (name, value) in args) map[name] = Argument(value);
            return Lookup(key, map);
        }

        public string Text(string key, params (string Name, Value Value)[] args)
        {
            var map = new Dictionary<string, IFluentType>(StringComparer.Ordinal);
            foreach (var (name, value) in args)
            {
                var bridged = ToBridge(value, NoEntities);
                map[name] = bridged == null ? new FluentString(CannotShow(value.KindName)) : Argument(bridged);
            }
            return Lookup(key, map);
        }

        /// <summary>
        /// 제목·선택지 자리 (G270): 문자열은 Def의 전체 이름이면 그 Def의 이름, 아니면 키. 쪽지는 번역, 엔티티는 그 Def의 이름,
        /// 숨김 값은 <c>engine-hidden</c>, <c>option</c>은 <c>label</c>이 있으면 그것 (G271). 그 밖은 <see cref="Value(BridgeValue)"/>와 같다.
        /// 경계 값의 엔티티는 제 Def를 들고 있다(G326, 못 보면 숨김).
        /// </summary>
        public string Label(BridgeValue value)
        {
            switch (value)
            {
                case BridgeStr s:
                    return IsDef(s.Value) ? DefText(s.Value) : Lookup(s.Value, null);
                case BridgeOption o:
                    return Label(o.Label ?? o.Value);
                default:
                    return Value(value);
            }
        }

        public string Label(Value value, WorldView? view = null) => Label(value, DefOf(view));

        public string Label(Value value, World world) => Label(value, DefOf(world));

        /// <summary>
        /// 그 값의 Fluent 속성 (G263·G270): 키면 <c>키.속성</c>, Def id·엔티티면 <c>def-….속성</c>, 쪽지면 그 키의 속성을 같은 인자로.
        /// <c>option</c>에 <c>desc</c>가 있고 속성이 <c>"desc"</c>면 그것을 쓴다.
        /// </summary>
        public string Attr(BridgeValue value, string attribute)
        {
            switch (value)
            {
                case BridgeStr s:
                    return Lookup((IsDef(s.Value) ? DefKey(s.Value) : s.Value) + "." + attribute, null);
                case BridgeEntity e:
                    return e.Def == null ? Hidden() : Lookup(DefKey(e.Def) + "." + attribute, null);
                case BridgeText t:
                    return Lookup(t.Key + "." + attribute, Arguments(t));
                case BridgeOption o:
                    return attribute == "desc" && o.Desc != null ? Label(o.Desc) : Attr(o.Value, attribute);
                case BridgeHidden _:
                    return Hidden();
                default:
                    Error("cannot look up ." + attribute + " of " + KindName(value));
                    return "";
            }
        }

        public string Attr(Value value, string attribute, WorldView? view = null) => Attr(value, attribute, DefOf(view));

        public string Attr(Value value, string attribute, World world) => Attr(value, attribute, DefOf(world));

        /// <summary>화면 값 안의 맨 값 하나 (G267·G269): 문자열은 Def의 전체 이름이 아니면 글자 그대로.</summary>
        public string Value(BridgeValue value)
        {
            switch (value)
            {
                case BridgeStr s:
                    return IsDef(s.Value) ? DefText(s.Value) : s.Value;
                case BridgeEntity e:
                    return e.Def == null ? Hidden() : DefText(e.Def);
                case BridgeText t:
                    return Format(t);
                case BridgeOption o:
                    return Label(o);
                case BridgeHidden _:
                    return Hidden();
                default:
                    return Plain(value) ?? CannotShow(KindName(value));
            }
        }

        public string Value(Value value, WorldView? view = null) => Value(value, DefOf(view));

        public string Value(Value value, World world) => Value(value, DefOf(world));

        // ---- 커널 값: 경계 값으로 바꿔 같은 규칙 ----

        private string Label(Value value, Func<EntityId, string?> defOf)
        {
            var bridged = ToBridge(value, defOf);
            return bridged == null ? CannotShow(value.KindName) : Label(bridged);
        }

        private string Attr(Value value, string attribute, Func<EntityId, string?> defOf)
        {
            var bridged = ToBridge(value, defOf);
            if (bridged != null) return Attr(bridged, attribute);
            Error("cannot look up ." + attribute + " of " + value.KindName);
            return "";
        }

        private string Value(Value value, Func<EntityId, string?> defOf)
        {
            var bridged = ToBridge(value, defOf);
            return bridged == null ? CannotShow(value.KindName) : Value(bridged);
        }

        /// <summary>경계를 넘을 수 없는 값(함수 등)이면 null.</summary>
        private static BridgeValue? ToBridge(Value value, Func<EntityId, string?> defOf)
        {
            try
            {
                return KernelValues.ToBridge(value, defOf);
            }
            catch (BridgeValueException)
            {
                return null;
            }
        }

        /// <summary>쪽지만 바꿀 때: 엔티티 인자는 <c>text()</c> 때 적어 둔 Def를 쓴다(G267).</summary>
        private static readonly Func<EntityId, string?> NoEntities = _ => null;

        // ---- 번역 인자 (G267) ----

        private Dictionary<string, IFluentType> Arguments(BridgeText text)
        {
            var map = new Dictionary<string, IFluentType>(StringComparer.Ordinal);
            foreach (var pair in text.Args) map[pair.Key] = Argument(pair.Value);
            return map;
        }

        /// <summary>엔티티 인자는 들고 있는 Def의 이름 (<c>text()</c> 때 적어 둔 것, G267).</summary>
        private IFluentType Argument(BridgeValue value)
        {
            switch (value)
            {
                case BridgeInt i:
                    return new ExactNumber(i.Value.ToString(CultureInfo.InvariantCulture));
                case BridgeFixed f:
                    return new ExactNumber(f.Decimal);
                case BridgeStr s:
                    return IsDef(s.Value) ? Def(s.Value) : new FluentString(s.Value);
                case BridgeText t:
                    return new FluentString(Format(t));
                case BridgeEntity e:
                    return e.Def == null ? new FluentString(Hidden()) : Def(e.Def);
                case BridgeHidden _:
                    return new FluentString(Hidden());
                default:
                    return new FluentString(Plain(value) ?? CannotShow(KindName(value)));
            }
        }

        /// <summary>bool·None은 <c>true</c>·<c>false</c>·<c>none</c>, 수는 십진 표기 (G267).</summary>
        private static string? Plain(BridgeValue value) => value switch
        {
            BridgeBool b => b.Value ? "true" : "false",
            BridgeNone _ => "none",
            BridgeInt i => i.Value.ToString(CultureInfo.InvariantCulture),
            BridgeFixed f => f.Decimal,
            _ => null,
        };

        /// <summary>커널의 값 종류 이름과 같은 글자(<c>list</c>, <c>dict</c> …).</summary>
        private static string KindName(BridgeValue value) => value.Kind.ToString().ToLowerInvariant();

        private string CannotShow(string kind)
        {
            Error("cannot show " + kind);
            return "";
        }

        // ---- 찾기 ----

        private bool IsDef(string id) => _translations.Defs.Contains(id);

        private string Hidden() => Lookup(EngineTexts.Hidden, null);

        /// <summary>번역 인자 자리의 Def: 이름과 함께 Def id를 들고 가 <c>JOSA</c>·<c>DEFATTR</c>가 쓴다 (G267·G268).</summary>
        private DefName Def(string defId) =>
            new DefName(defId, DefText(defId), attribute => TryLookup(DefKey(defId) + "." + attribute, null));

        /// <summary>Def의 화면 이름. 어느 언어에도 없으면 키(<c>def-…</c>)가 아니라 Def id를 보인다 (G275).</summary>
        private string DefText(string defId)
        {
            string key = DefKey(defId);
            var text = TryLookup(key, null);
            if (text != null) return text;
            Error("no translation for '" + key + "'");
            return defId;
        }

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
