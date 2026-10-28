using System;
using System.Collections.Generic;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Loading;
using Kesa.Engine.Script;
using Linguini.Syntax.Ast;

namespace Kesa.Engine.Text
{
    /// <summary>
    /// <c>check</c>의 번역 검사 (spec-gaps G257·G262·G268·G273). 번역 파일 읽기의 오류(<c>LOC001~005</c>)는 <see cref="Translations.Load"/>가 낸다.
    /// 모두 번역 오류라 모드를 끄지 않는다. 키를 계산한 <c>text()</c>와 아무도 쓰지 않는 글은 보지 않는다
    /// (호스트 UI 파일도 키를 쓰는데 엔진은 그 파일을 읽지 않음, G249).
    /// </summary>
    public static class TranslationCheck
    {
        public const string DefPrefix = "def-";
        public const string EnginePrefix = EngineTexts.Owner + "-";

        public static void Run(Translations texts, ContentLoadResult content, DiagnosticBag diagnostics)
        {
            var defKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var def in content.Expanded.Defs) defKeys.Add(Translator.DefKey(def.Id));

            foreach (var language in texts.Entries)
            foreach (var pair in language.Value)
            {
                var entry = pair.Value;
                if (entry.Mod == null || entry.Mod == EngineTexts.Owner) continue; // 엔진·호스트의 글은 모더가 고칠 수 없다
                ReservedKey(pair.Key, entry, defKeys, texts, diagnostics);
                foreach (var pattern in Patterns(entry.Item))
                    foreach (var particle in Particles(pattern.Value))
                        if (!Josa.IsKnown(particle))
                            diagnostics.Error(Codes.LocUnknownParticle, "JOSA in '" + pair.Key + "' (" + language.Key + ") has no particle '" +
                                particle + "'", entry.Span, entry.Mod,
                                hint: "known particles: " + string.Join(" ", Josa.Known()));
                foreach (var pattern in Patterns(entry.Item))
                    foreach (var problem in NumberProblems(pattern.Value))
                        diagnostics.Error(Codes.LocNumberOption, "NUMBER in '" + pair.Key + "' (" + language.Key + "): " + problem,
                            entry.Span, entry.Mod, hint: "NUMBER takes one number and the options " + NumberFunction.Known);
            }

            ScriptUses(texts, content, diagnostics);
            OtherLanguageVariables(texts, diagnostics);
            Untranslated(texts, content, diagnostics);
        }

        // ---- 엔진 예약 접두사 (G262·G273) ----

        private static void ReservedKey(string key, Translations.Entry entry, HashSet<string> defKeys, Translations texts,
            DiagnosticBag diagnostics)
        {
            if (key.StartsWith(DefPrefix, StringComparison.Ordinal) && !defKeys.Contains(key))
                diagnostics.Error(Codes.LocReservedKey, "'" + key + "' starts with " + DefPrefix + " but names no loaded Def", entry.Span,
                    entry.Mod, hint: "def- keys are the names of Defs (harbor:rigs/Brig is def-harbor-rigs-Brig); give other texts another key");
            else if (key.StartsWith(EnginePrefix, StringComparison.Ordinal) && !texts.ProvidedKeys.Contains(key))
                diagnostics.Error(Codes.LocReservedKey, "'" + key + "' starts with " + EnginePrefix + ", which is reserved for the engine and hosts",
                    entry.Span, entry.Mod, hint: "a mod may only replace engine- texts the engine or the host defines; start the key with the mod id");
        }

        // ---- 스크립트의 text("키") ----

        private static void ScriptUses(Translations texts, ContentLoadResult content, DiagnosticBag diagnostics)
        {
            foreach (var mod in content.TextUses)
            foreach (var use in mod.Value)
            {
                bool found = false;
                var passed = new HashSet<string>(use.ArgumentNames, StringComparer.Ordinal);
                foreach (var language in texts.Entries)
                {
                    var pattern = Find(language.Value, use.Key);
                    if (pattern == null) continue;
                    found = true;
                    var missing = new SortedSet<string>(StringComparer.Ordinal);
                    foreach (var v in Variables(pattern)) if (!passed.Contains(v)) missing.Add(v);
                    if (missing.Count > 0)
                        diagnostics.Warning(Codes.LocMissingArgument,
                            "text(\"" + use.Key + "\") does not pass " + Names(missing) + ", which the " + language.Key + " text uses",
                            use.Span, mod.Key, hint: "pass it as text(\"" + use.Key + "\", " + First(missing) + "=...)");
                }
                if (!found)
                    diagnostics.Warning(Codes.LocMissingKey, "text(\"" + use.Key + "\") has no translation in any language", use.Span, mod.Key,
                        hint: "add '" + use.Key + " = ...' to a .ftl file under locale/<language>/");
            }
        }

        // ---- 언어 사이 (주인의 기본 언어가 기준) ----

        private static void OtherLanguageVariables(Translations texts, DiagnosticBag diagnostics)
        {
            foreach (var language in texts.Entries)
            foreach (var pair in language.Value)
            {
                var entry = pair.Value;
                if (entry.Mod == null || entry.Mod == EngineTexts.Owner) continue;
                if (!texts.OwnerLanguage(pair.Key, out var home) || home == language.Key) continue;
                if (!texts.Entries.TryGetValue(home, out var homeTable) || !homeTable.TryGetValue(pair.Key, out var homeEntry)) continue;
                var homePatterns = Patterns(homeEntry.Item);
                foreach (var pattern in Patterns(entry.Item))
                {
                    var known = new HashSet<string>(StringComparer.Ordinal);
                    if (homePatterns.TryGetValue(pattern.Key, out var homePattern)) known.UnionWith(Variables(homePattern));
                    else continue; // 기본 언어에 없는 속성은 따로 알리지 않는다
                    var extra = new SortedSet<string>(StringComparer.Ordinal);
                    foreach (var v in Variables(pattern.Value)) if (!known.Contains(v)) extra.Add(v);
                    if (extra.Count > 0)
                        diagnostics.Warning(Codes.LocExtraVariable,
                            "the " + language.Key + " text of '" + pattern.Key + "' uses " + Names(extra) + ", which the " + home +
                            " text (the default language) does not", entry.Span, entry.Mod,
                            hint: "scripts are written against the default language; the value may never be passed");
                }
            }
        }

        /// <summary>모드·언어마다 그 모드가 주인인 글 중 빠진 수 (정보 한 줄, 목록은 덤프).</summary>
        private static void Untranslated(Translations texts, ContentLoadResult content, DiagnosticBag diagnostics)
        {
            // 엔진 글만 있는 언어는 셈하지 않는다
            var languages = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var language in texts.Entries)
                foreach (var entry in language.Value.Values)
                    if (entry.Mod != EngineTexts.Owner) { languages.Add(language.Key); break; }
            foreach (var mod in content.Mods)
            {
                var owned = new List<string>();
                foreach (var pair in texts.OwnerMods)
                    if (pair.Value == mod.Id) owned.Add(pair.Key);
                if (owned.Count == 0) continue;
                foreach (var language in languages)
                {
                    var table = texts.Entries[language];
                    int missing = 0;
                    foreach (var key in owned) if (!table.ContainsKey(key)) missing++;
                    if (missing > 0)
                        diagnostics.Info(Codes.LocUntranslated, "mod '" + mod.Id + "' has " + missing + " of " + owned.Count +
                            " texts untranslated in " + language, new SourceSpan(mod.Directory + "/" + Translations.LocaleFolder, 0, 0), mod.Id);
                }
            }
        }

        // ---- Fluent 글 훑기 ----

        /// <summary><c>키</c>나 <c>키.속성</c>의 글, 없으면 null.</summary>
        private static Pattern? Find(Dictionary<string, Translations.Entry> table, string key)
        {
            int dot = key.IndexOf('.');
            string id = dot < 0 ? key : key.Substring(0, dot);
            if (!table.TryGetValue(id, out var entry)) return null;
            return Patterns(entry.Item).TryGetValue(key, out var pattern) ? pattern : null;
        }

        /// <summary>글의 값과 속성: <c>키</c> → 값, <c>키.속성</c> → 속성 값.</summary>
        private static Dictionary<string, Pattern> Patterns(IEntry item)
        {
            var map = new Dictionary<string, Pattern>(StringComparer.Ordinal);
            string id;
            Pattern? value;
            List<Linguini.Syntax.Ast.Attribute> attributes;
            if (item is AstMessage m)
            {
                id = m.GetId();
                value = m.Value;
                attributes = m.Attributes;
            }
            else if (item is AstTerm t)
            {
                id = "-" + t.GetId();
                value = t.Value;
                attributes = t.Attributes;
            }
            else return map;
            if (value != null) map[id] = value;
            foreach (var a in attributes) map[id + "." + a.Id.Name.ToString()] = a.Value;
            return map;
        }

        private static IEnumerable<string> Variables(Pattern pattern)
        {
            var found = new SortedSet<string>(StringComparer.Ordinal);
            Walk(pattern, e =>
            {
                if (e is VariableReference v) found.Add(v.Id.Name.ToString());
            });
            return found;
        }

        /// <summary>글 안의 <c>JOSA(…, "조사")</c>에 리터럴로 적은 조사.</summary>
        private static IEnumerable<string> Particles(Pattern pattern)
        {
            var found = new List<string>();
            Walk(pattern, e =>
            {
                if (e is FunctionReference f && f.Id.Name.ToString() == TextFunctions.JosaName &&
                    f.Arguments.PositionalArgs.Count == 2 && f.Arguments.PositionalArgs[1] is TextLiteral literal)
                    found.Add(literal.Value.ToString());
            });
            return found;
        }

        /// <summary>글 안의 <c>NUMBER(…)</c>에 리터럴로 적은 옵션 중 맞지 않는 것 (G356). 변수로 준 값은 실행 때 본다.</summary>
        private static IEnumerable<string> NumberProblems(Pattern pattern)
        {
            var found = new List<string>();
            Walk(pattern, e =>
            {
                if (!(e is FunctionReference f) || f.Id.Name.ToString() != NumberFunction.Name) return;
                if (f.Arguments.PositionalArgs.Count != 1)
                    found.Add("takes one number, got " + f.Arguments.PositionalArgs.Count);
                int? min = null, max = null;
                foreach (var a in f.Arguments.NamedArgs)
                {
                    string name = a.Name.Name.ToString();
                    string? value = a.Value switch
                    {
                        TextLiteral t => t.Value.ToString(),
                        NumberLiteral n => n.Value.ToString(),
                        _ => null,
                    };
                    var problem = value == null ? null : NumberFunction.Problem(name, value);
                    if (problem != null) found.Add(problem);
                    else if (value != null && name == "minimumFractionDigits") min = int.Parse(value);
                    else if (value != null && name == "maximumFractionDigits") max = int.Parse(value);
                }
                var range = min != null && max != null ? NumberFunction.RangeProblem(min.Value, max.Value) : null;
                if (range != null) found.Add(range);
            });
            return found;
        }

        private static void Walk(Pattern pattern, Action<IExpression> visit)
        {
            foreach (var element in pattern.Elements)
                if (element is Placeable p) Walk(p.Expression, visit);
        }

        private static void Walk(IExpression expression, Action<IExpression> visit)
        {
            visit(expression);
            switch (expression)
            {
                case Placeable p:
                    Walk(p.Expression, visit);
                    break;
                case SelectExpression s:
                    Walk(s.Selector, visit);
                    foreach (var variant in s.Variants) Walk(variant.Value, visit);
                    break;
                case FunctionReference f:
                    Walk(f.Arguments, visit);
                    break;
                case TermReference t when t.Arguments != null:
                    Walk(t.Arguments.Value, visit);
                    break;
            }
        }

        private static void Walk(CallArguments args, Action<IExpression> visit)
        {
            foreach (var a in args.PositionalArgs) Walk(a, visit);
            foreach (var a in args.NamedArgs) Walk(a.Value, visit);
        }

        private static string Names(SortedSet<string> names)
        {
            var parts = new List<string>();
            foreach (var n in names) parts.Add("$" + n);
            return string.Join(", ", parts);
        }

        private static string First(SortedSet<string> names)
        {
            foreach (var n in names) return n;
            return "";
        }
    }
}
