#nullable enable
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kesa.Engine.Bridge;
using Kesa.Engine.Bridge.Local;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Loading;
using Kesa.Engine.Runtime;
using Kesa.Engine.Text;
using UnityEngine;

namespace Kesa.Engine.Unity
{
    /// <summary>
    /// 불러온 모드 (spec-gaps G323·G353): 내용, 번역기, 화면 파일 찾기와 불러오며 난 진단. 판은 <see cref="NewMatch"/>로 여러 번 연다.
    /// 화면은 <see cref="UiToolkitRenderer"/>와 <c>UiInterpreter</c>가 그린다.
    /// </summary>
    public sealed class LoadedMods
    {
        private readonly ContentLoadResult _content;

        private LoadedMods(ContentLoadResult content, Translator translator, ModFolderAssets assets, IReadOnlyList<string> modFolders,
            IReadOnlyList<Diagnostic> diagnostics)
        {
            _content = content;
            Translator = translator;
            Assets = assets;
            ModFolders = modFolders;
            Diagnostics = diagnostics;
        }

        public Translator Translator { get; }

        public ModFolderAssets Assets { get; }

        /// <summary>살아남은 모드의 폴더, 로드 순서대로.</summary>
        public IReadOnlyList<string> ModFolders { get; }

        /// <summary>모드와 번역을 불러오며 난 진단(정렬됨). 보일지, 오류면 멈출지는 게임이 정한다.</summary>
        public IReadOnlyList<Diagnostic> Diagnostics { get; }

        /// <summary>오류로 빠진 모드 id (G10·G11·G28·G49). 이유는 <see cref="Diagnostics"/>에 있다.</summary>
        public IReadOnlyList<string> Disabled => _content.Disabled;

        public bool HasErrors => Diagnostics.Any(d => d.Severity == Severity.Error);

        /// <summary>글자 속성에 들어갈 값을 번역해 글자로 (UiInterpreter에 꽂는다, G345).</summary>
        public string Format(BridgeValue value) => Translator.Value(value);

        /// <summary>
        /// <paramref name="modsRoot"/>의 모드를 불러온다. 오류가 난 모드는 빼고 남은 모드로 만들며 던지지 않는다 (G353).
        /// </summary>
        public static LoadedMods Load(string modsRoot, IReadOnlyList<string> languages, string? hostLocaleFolder = null)
        {
            var bag = new DiagnosticBag();
            var order = ModSorter.Sort(ModDiscovery.Discover(modsRoot, bag), bag);
            var content = ContentLoader.Load(modsRoot, order, bag);
            var translations = Translations.Load(content, bag, hostLocaleFolder);
            var translator = translations.For(languages, message => Debug.LogWarning("Kesa translation: " + message));
            var folders = content.Mods.Select(m => Path.Combine(content.ModsRoot, m.Directory)).ToList();
            return new LoadedMods(content, translator, new ModFolderAssets(folders), folders, bag.ToSortedList());
        }

        /// <summary>이 모드로 새 판을 연다. 모드를 다시 읽지 않는다.</summary>
        public Match NewMatch(int seed) => new Match(this, new LocalHost(_content, new World(_content, seed: seed)));
    }
}
