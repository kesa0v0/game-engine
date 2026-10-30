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
using Kesa.Engine.Ui;
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
            IReadOnlyList<Diagnostic> diagnostics, LookLibrary looks, InputLibrary inputs)
        {
            Looks = looks;
            Inputs = inputs;
            _content = content;
            Translator = translator;
            Assets = assets;
            ModFolders = modFolders;
            Diagnostics = diagnostics;
        }

        public Translator Translator { get; }

        /// <summary>
        /// 모드들의 그림 정의(<c>*.look.xml</c>)와 공간(<c>*.space.xml</c>) (G409~G411). 읽다 난 문제는 <see cref="LookLibrary.Problems"/>에 있고
        /// 틀린 정의만 빠진다.
        /// </summary>
        public LookLibrary Looks { get; }

        /// <summary>
        /// 모드들의 입력 선언(<c>*.input.xml</c>, G417·G423). 키 이름은 Input System <c>Key</c>이고 읽다 난 문제는 <see cref="InputLibrary.Problems"/>에 있다.
        /// 키를 누르면 이름을 내는 것은 <see cref="ModInputs"/>다.
        /// </summary>
        public InputLibrary Inputs { get; }

        public ModFolderAssets Assets { get; }

        /// <summary>살아남은 모드의 폴더, 로드 순서대로.</summary>
        public IReadOnlyList<string> ModFolders { get; }

        /// <summary>모드와 번역을 불러오며 난 진단(정렬됨). 보일지, 오류면 멈출지는 게임이 정한다.</summary>
        public IReadOnlyList<Diagnostic> Diagnostics { get; }

        /// <summary>오류로 빠진 모드 id (G10·G11·G28·G49). 이유는 <see cref="Diagnostics"/>에 있다.</summary>
        public IReadOnlyList<string> Disabled => _content.Disabled;

        public bool HasErrors => Diagnostics.Any(d => d.Severity == Severity.Error);

        /// <summary>연출 방식 (G429): 로드 순서가 뒤인 모드의 <c>Mod(presentation=…)</c>, 없으면 <see cref="BridgePresentation.Instant"/>.</summary>
        public string Presentation => _content.Mods.LastOrDefault(m => m.Presentation != null)?.Presentation ?? BridgePresentation.Instant;

        /// <summary>글자 속성에 들어갈 값을 번역해 글자로 (UiInterpreter에 꽂는다, G345).</summary>
        public string Format(BridgeValue value) => Translator.Value(value);

        /// <summary>
        /// <paramref name="modsRoot"/>의 모드를 불러온다. 오류가 난 모드는 빼고 남은 모드로 만들며 던지지 않는다 (G353).
        /// <paramref name="lookKinds"/>는 그림 요소·공간 종류(G413). 없으면 기본 종류(glTF 모델 포함)이고, 게임은 <see cref="GameObjectLookRenderer.StandardKinds"/>에 제 종류를 더해 넘긴다.
        /// </summary>
        public static LoadedMods Load(string modsRoot, IReadOnlyList<string> languages, string? hostLocaleFolder = null, LookKinds? lookKinds = null)
        {
            var bag = new DiagnosticBag();
            var order = ModSorter.Sort(ModDiscovery.Discover(modsRoot, bag), bag);
            var content = ContentLoader.Load(modsRoot, order, bag);
            var translations = Translations.Load(content, bag, hostLocaleFolder);
            var translator = translations.For(languages, message => Debug.LogWarning("Kesa translation: " + message));
            var folders = content.Mods.Select(m => Path.Combine(content.ModsRoot, m.Directory)).ToList();
            var assets = new ModFolderAssets(folders);
            var sources = HostSources(content).ToList();
            var looks = LookLibrary.Load(sources, lookKinds ?? GameObjectLookRenderer.StandardKinds(), path => assets.Find(path) != null);
            var inputs = InputLibrary.Load(sources, ModInputs.KeyNames);
            return new LoadedMods(content, translator, assets, folders, bag.ToSortedList(), looks, inputs);
        }

        // 확장자로 알아보므로 모드 폴더 어디에 있어도 읽는다 (G409·G411·G417). 한 모드 안은 경로 순
        private static IEnumerable<LookSource> HostSources(ContentLoadResult content)
        {
            foreach (var mod in content.Mods)
            {
                string folder = Path.Combine(content.ModsRoot, mod.Directory);
                var files = Directory.GetFiles(folder, "*.xml", SearchOption.AllDirectories)
                    .Select(f => Path.GetRelativePath(folder, f).Replace('\\', '/'))
                    .Where(f => HostExtensions.Any(e => f.EndsWith(e, System.StringComparison.OrdinalIgnoreCase)))
                    .OrderBy(f => f, System.StringComparer.Ordinal);
                foreach (var file in files) yield return new LookSource(mod.Id, mod.Id + "/" + file, File.ReadAllText(Path.Combine(folder, file)));
            }
        }

        private static readonly string[] HostExtensions = { ".look.xml", ".space.xml", InputLibrary.Extension };

        /// <summary>이 모드로 새 판을 연다. 모드를 다시 읽지 않는다.</summary>
        public Match NewMatch(int seed) => new Match(this, new LocalHost(_content, new World(_content, seed: seed)));
    }
}
