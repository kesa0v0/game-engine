#nullable enable
using System;
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
    /// 한 판을 같은 프로세스에서 돌리는 묶음 (spec-gaps G323·G324): 모드 폴더를 불러 <see cref="LocalHost"/>를 만들고, 번역기와 화면 파일 찾기를
    /// 갖춘다. 화면은 <see cref="UiToolkitRenderer"/>와 <c>UiInterpreter</c>가 그린다.
    /// </summary>
    public sealed class KesaGame
    {
        private KesaGame(LocalHost host, Translator translator, ModFolderAssets assets, IReadOnlyList<string> modFolders)
        {
            Host = host;
            Translator = translator;
            Assets = assets;
            ModFolders = modFolders;
        }

        public LocalHost Host { get; }

        public Translator Translator { get; }

        public ModFolderAssets Assets { get; }

        /// <summary>모드 폴더, 로드 순서대로.</summary>
        public IReadOnlyList<string> ModFolders { get; }

        /// <summary>글자 속성에 들어갈 값을 번역해 글자로 (UiInterpreter에 꽂는다, G345).</summary>
        public string Format(BridgeValue value) => Translator.Value(value);

        /// <summary>
        /// <paramref name="modsRoot"/>의 모드를 불러 새 판을 만든다. 모드에 오류가 있으면 <see cref="InvalidOperationException"/>이다.
        /// 번역 오류는 판을 막지 않고 경고로 남긴다 (G257).
        /// </summary>
        public static KesaGame Load(string modsRoot, int seed, IReadOnlyList<string> languages, string? hostLocaleFolder = null)
        {
            var bag = new DiagnosticBag();
            var order = ModSorter.Sort(ModDiscovery.Discover(modsRoot, bag), bag);
            var content = ContentLoader.Load(modsRoot, order, bag);
            var errors = bag.ToSortedList().Where(d => d.Severity == Severity.Error).ToList();
            if (errors.Count > 0)
                throw new InvalidOperationException("the mods in '" + modsRoot + "' have errors:\n" + string.Join("\n", errors));

            var host = new LocalHost(content, new World(content, seed: seed));
            var bridge = host.Content().Result;
            var textBag = new DiagnosticBag();
            var translations = Translations.Load(bridge, textBag, hostLocaleFolder);
            foreach (var d in textBag.ToSortedList())
                if (d.Severity != Severity.Info) Debug.LogWarning("Kesa translation: " + d);
            var translator = translations.For(languages, message => Debug.LogWarning("Kesa translation: " + message));

            var folders = bridge.Mods.Select(m => m.Folder).ToList();
            return new KesaGame(host, translator, new ModFolderAssets(folders), folders);
        }
    }
}
