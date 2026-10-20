using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Loading;
using Linguini.Bundle;
using Linguini.Bundle.Builder;
using Linguini.Syntax.Ast;
using Linguini.Syntax.Parser;

namespace Kesa.Engine.Text
{
    /// <summary>
    /// 불린 모드들의 번역 파일 전부 (spec X4, spec-gaps G254·G256·G257·G269). 만든 뒤 바뀌지 않는다.
    /// 번역 파일을 고치면 호스트가 <see cref="Load"/>를 다시 불러 새 것으로 갈아 끼운다 (G269 다시 읽기).
    /// 플레이어마다 <see cref="For"/>로 언어 목록에 맞춘 <see cref="Translator"/>를 얻는다.
    /// </summary>
    public sealed class Translations
    {
        /// <summary>모드 폴더 안 번역 파일의 자리: <c>locale/&lt;언어 태그&gt;/**/*.ftl</c> (G256).</summary>
        public const string LocaleFolder = "locale";

        private readonly IReadOnlyDictionary<string, FluentBundle> _bundles;
        private readonly IReadOnlyDictionary<string, string> _ownerLanguages;

        private Translations(IReadOnlyDictionary<string, FluentBundle> bundles, IReadOnlyDictionary<string, string> ownerLanguages,
            DefSet defs)
        {
            _bundles = bundles;
            _ownerLanguages = ownerLanguages;
            Defs = defs;
        }

        /// <summary>불린 Def: 문자열이 Def의 전체 이름과 같으면 그 Def의 이름을 보인다 (G267·G270).</summary>
        internal DefSet Defs { get; }

        /// <summary>번역 파일이 있는 언어 태그 (서수 순).</summary>
        public IEnumerable<string> Languages => _bundles.Keys;

        /// <summary>
        /// 엔진의 기본 글 → 호스트의 폴더 → 살아남은 모드를 로드 순서대로 읽는다. 같은 언어의 같은 키는 나중이 이긴다 (G256).
        /// 오류는 <paramref name="diagnostics"/>에 적고 그 글만 뺀다. 모드를 끄지 않는다 (G257).
        /// </summary>
        /// <param name="hostFolder">호스트 제 번역 폴더 (<c>&lt;언어 태그&gt;/**/*.ftl</c>), 없으면 null</param>
        /// <param name="hostLanguage">호스트 글의 기본 언어 (폴백 G257)</param>
        public static Translations Load(ContentLoadResult content, DiagnosticBag diagnostics, string? hostFolder = null,
            string hostLanguage = EngineTexts.Language)
        {
            var merge = new Merge(content.Order, diagnostics);
            foreach (var pair in EngineTexts.Files)
                merge.AddFile(EngineTexts.Owner, EngineTexts.Language, pair.Key, "<engine>/" + pair.Key + ".ftl", pair.Value);
            if (hostFolder != null && Directory.Exists(hostFolder))
                merge.AddFolder(null, hostLanguage, hostFolder, hostFolder.Replace('\\', '/'));
            foreach (var mod in content.Mods)
            {
                string modFolder = Path.Combine(content.ModsRoot, mod.Directory);
                string folder = Path.Combine(modFolder, LocaleFolder);
                if (!Directory.Exists(folder)) continue;
                var languages = merge.AddFolder(mod.Id, mod.DefaultLanguage, folder, mod.Directory + "/" + LocaleFolder);
                // locale/이 있으면 defaultLanguage를 적고 그 폴더를 가져야 한다 (G257)
                if (mod.DefaultLanguage == null)
                    diagnostics.Error(Codes.LocNoDefaultLanguage, "mod '" + mod.Id + "' has " + LocaleFolder + "/ but no defaultLanguage",
                        new SourceSpan(mod.Directory + "/" + ModDiscovery.ManifestFileName, 1, 1), mod.Id,
                        hint: "add defaultLanguage=\"en\" (the language its texts fall back to) to Mod(...)");
                else if (!languages.Contains(mod.DefaultLanguage))
                    diagnostics.Error(Codes.LocNoDefaultLanguage,
                        "mod '" + mod.Id + "' has defaultLanguage '" + mod.DefaultLanguage + "' but no " + LocaleFolder + "/" +
                        mod.DefaultLanguage + "/ folder", new SourceSpan(mod.Directory + "/" + ModDiscovery.ManifestFileName, 1, 1),
                        mod.Id);
            }
            return new Translations(merge.Build(), merge.OwnerLanguages, content.Frozen);
        }

        /// <summary>플레이어의 언어 목록(앞이 먼저, 예: <c>["ko", "en"]</c>)에 맞춘 번역기 (G257). 언어를 바꾸면 새로 얻는다.</summary>
        /// <param name="onError">번역 오류(없는 키, 빠진 인자 등)를 받는 곳. 결과 글은 오류가 나도 늘 나온다 (G269)</param>
        public Translator For(IReadOnlyList<string> languages, Action<string>? onError = null)
        {
            var chain = new List<FluentBundle>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string language in languages)
            {
                // 태그를 줄인 것까지: ko-KR → ko (G257)
                for (string? tag = language; tag != null; tag = LanguageTag.Parent(tag))
                    if (seen.Add(tag) && _bundles.TryGetValue(tag, out var bundle)) chain.Add(bundle);
            }
            return new Translator(chain, this, onError);
        }

        /// <summary>그 키의 주인(로드 순서상 처음 정의한 곳)의 기본 언어 번들, 없으면 null (G257 폴백 두 번째).</summary>
        internal FluentBundle? OwnerBundle(string key)
        {
            if (!_ownerLanguages.TryGetValue(key, out var language)) return null;
            return _bundles.TryGetValue(language, out var bundle) ? bundle : null;
        }

        // ---- 모으기 ----

        /// <summary>한 번의 <see cref="Load"/>: 언어 → 키 → 이긴 글, 키 → 주인의 기본 언어.</summary>
        private sealed class Merge
        {
            private readonly LoadOrder? _order;
            private readonly DiagnosticBag _diagnostics;
            private readonly SortedDictionary<string, Dictionary<string, Entry>> _byLanguage =
                new SortedDictionary<string, Dictionary<string, Entry>>(StringComparer.Ordinal);

            public Merge(LoadOrder? order, DiagnosticBag diagnostics)
            {
                _order = order;
                _diagnostics = diagnostics;
            }

            public Dictionary<string, string> OwnerLanguages { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

            /// <summary><c>&lt;태그&gt;/**/*.ftl</c>을 읽는다. 언어 폴더마다 상대 경로의 서수 순 (G18·G256). 읽은 언어 태그를 돌려준다.</summary>
            /// <param name="mod">모드 id, 호스트는 null</param>
            public HashSet<string> AddFolder(string? mod, string? defaultLanguage, string folder, string shown)
            {
                var languages = new HashSet<string>(StringComparer.Ordinal);
                var dirs = Directory.GetDirectories(folder);
                Array.Sort(dirs, StringComparer.Ordinal);
                foreach (string dir in dirs)
                {
                    string language = Path.GetFileName(dir);
                    if (!LanguageTag.IsValid(language))
                    {
                        _diagnostics.Error(Codes.LocBadLanguage, "translation folder '" + language + "' is not a language tag",
                            new SourceSpan(shown + "/" + language, 0, 0), mod, hint: LanguageTag.Rule);
                        continue;
                    }
                    languages.Add(language);
                    var files = new List<string>(Directory.GetFiles(dir, "*.ftl", SearchOption.AllDirectories));
                    var relative = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (string f in files) relative[f] = f.Substring(dir.Length + 1).Replace('\\', '/');
                    files.Sort((a, b) => string.CompareOrdinal(relative[a], relative[b]));
                    foreach (string f in files)
                        AddFile(mod, defaultLanguage, language, shown + "/" + language + "/" + relative[f], File.ReadAllText(f));
                }
                return languages;
            }

            /// <summary>파일 하나: 구문 오류는 알리고 그 글(Junk)만 뺀다 (G257).</summary>
            public void AddFile(string? mod, string? defaultLanguage, string language, string path, string text)
            {
                var resource = LinguiniParser.FromFragment(text, path).Parse();
                foreach (var error in resource.Errors)
                {
                    // Row는 파서가 멈춘 줄(다음 글)이라 빠진 글(Slice)이 시작하는 줄을 댄다
                    int at = error.Slice?.Start.Value ?? error.Position.Start.Value;
                    _diagnostics.Error(Codes.LocSyntax, "translation syntax error: " + error.Message,
                        new SourceSpan(path, LineAt(text, at), 1), mod);
                }
                if (!_byLanguage.TryGetValue(language, out var table))
                    _byLanguage[language] = table = new Dictionary<string, Entry>(StringComparer.Ordinal);
                string[]? lines = null;
                foreach (var item in resource.Entries)
                {
                    string key;
                    if (item is AstMessage message) key = message.GetId();
                    else if (item is AstTerm term) key = "-" + term.GetId();
                    else continue;
                    lines ??= text.Split('\n');
                    var entry = new Entry(item, mod, new SourceSpan(path, LineOf(lines, key), 1));
                    if (table.TryGetValue(key, out var before))
                    {
                        if (before.Mod == mod)
                        {
                            // 한 곳 안의 같은 키는 오류, 앞의 것을 남긴다 (G256)
                            _diagnostics.Error(Codes.LocDuplicate, "'" + key + "' is already defined for " + language + " at " + before.Span,
                                entry.Span, mod, related: new[] { before.Span });
                            continue;
                        }
                        // 서로 순서가 없는 두 모드면 알린다. 나중이 이기는 것은 같다 (G256)
                        if (before.Mod != null && mod != null && before.Mod != EngineTexts.Owner &&
                            (_order == null || !_order.IsOrdered(before.Mod, mod)))
                            _diagnostics.Warning(Codes.LocOverridden,
                                "mods '" + before.Mod + "' and '" + mod + "' both define '" + key + "' for " + language +
                                " and have no load order between them; '" + mod + "' wins", entry.Span, mod,
                                hint: "if this mod translates '" + before.Mod + "', list it in loadAfter", related: new[] { before.Span });
                    }
                    table[key] = entry;
                    if (!OwnerLanguages.ContainsKey(key) && defaultLanguage != null) OwnerLanguages[key] = defaultLanguage;
                }
            }

            public IReadOnlyDictionary<string, FluentBundle> Build()
            {
                var bundles = new SortedDictionary<string, FluentBundle>(StringComparer.Ordinal);
                foreach (var pair in _byLanguage)
                {
                    // 겹침은 이미 골랐으니 이긴 글만 한 덩이로 넣는다
                    var entries = new List<IEntry>();
                    foreach (var entry in pair.Value.Values) entries.Add(entry.Item);
                    var bundle = NewBundle(pair.Key);
                    bundle.AddResourceOverriding(new Resource(entries, new List<Linguini.Syntax.Parser.Error.ParseError>()));
                    TextFunctions.AddTo(bundle);
                    bundles[pair.Key] = bundle;
                }
                return bundles;
            }

            private static FluentBundle NewBundle(string language)
            {
                CultureInfo culture;
                try
                {
                    culture = new CultureInfo(language);
                }
                catch (CultureNotFoundException)
                {
                    culture = CultureInfo.InvariantCulture; // 복수형 규칙을 모르는 언어
                }
                // 자리 표시자를 U+2068·U+2069로 감싸지 않는다: 조사가 이름 바로 뒤에 붙어야 한다
                return LinguiniBuilder.Builder().CultureInfo(culture).SkipResources().SetUseIsolating(false).UncheckedBuild();
            }

            private static int LineAt(string text, int offset)
            {
                int line = 1;
                for (int i = 0; i < offset && i < text.Length; i++)
                    if (text[i] == '\n') line++;
                return line;
            }

            /// <summary>Fluent의 글은 줄 첫 칸에서 시작한다 (<c>키 =</c>). 찾지 못하면 0.</summary>
            private static int LineOf(string[] lines, string key)
            {
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (!line.StartsWith(key, StringComparison.Ordinal)) continue;
                    string rest = line.Substring(key.Length).TrimStart(' ');
                    if (rest.StartsWith("=", StringComparison.Ordinal)) return i + 1;
                }
                return 0;
            }
        }

        private sealed class Entry
        {
            public Entry(IEntry item, string? mod, SourceSpan span)
            {
                Item = item;
                Mod = mod;
                Span = span;
            }

            public IEntry Item { get; }

            /// <summary>모드 id, 엔진은 <see cref="EngineTexts.Owner"/>, 호스트는 null.</summary>
            public string? Mod { get; }

            public SourceSpan Span { get; }
        }
    }
}
