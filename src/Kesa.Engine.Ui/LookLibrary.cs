using System;
using System.Collections.Generic;

namespace Kesa.Engine.Ui
{
    /// <summary>
    /// 모드들의 그림 정의와 공간 선언을 로드 순서대로 모은 것 (G390·G392·G409~G411). 같은 이름은 뒤 모드가 통째로 이기고, 한 모드 안의
    /// 같은 이름은 문제다. 에셋은 읽지 않는다(G392: 처음 보일 때 렌더러가 읽음).
    /// </summary>
    public sealed class LookLibrary
    {
        private LookLibrary(IReadOnlyDictionary<string, LookDefinition> looks, IReadOnlyDictionary<string, SpaceDeclaration> spaces,
            IReadOnlyList<UiProblem> problems)
        {
            Looks = looks;
            Spaces = spaces;
            Problems = problems;
        }

        public IReadOnlyDictionary<string, LookDefinition> Looks { get; }

        public IReadOnlyDictionary<string, SpaceDeclaration> Spaces { get; }

        /// <summary>읽다 난 문제. 틀린 정의·선언만 빠지고 나머지는 쓴다.</summary>
        public IReadOnlyList<UiProblem> Problems { get; }

        /// <summary>
        /// <paramref name="sources"/>는 모드의 로드 순서대로다. 확장자가 <c>.look.xml</c>·<c>.space.xml</c>이 아닌 파일은 건너뛴다.
        /// </summary>
        /// <param name="fileExists">적어 둔 그림 경로(G335 가상 경로)가 있는지. 주면 없는 파일을 로드 때 알린다(G392). 묶인 경로는 그릴 때 안다.</param>
        public static LookLibrary Load(IEnumerable<LookSource> sources, LookKinds kinds, Func<string, bool>? fileExists = null)
        {
            var problems = new List<UiProblem>();
            var looks = new SortedDictionary<string, LookDefinition>(StringComparer.Ordinal);
            var spaces = new SortedDictionary<string, SpaceDeclaration>(StringComparer.Ordinal);
            foreach (var source in sources)
            {
                if (source.Path.EndsWith(LookReader.LookExtension, StringComparison.OrdinalIgnoreCase))
                {
                    var read = new List<LookDefinition>();
                    LookReader.ReadLooks(source, kinds, read, problems);
                    if (fileExists != null)
                        foreach (var look in read) CheckFiles(look.Elements, fileExists, problems);
                    foreach (var look in read) Put(looks, look.Name, look, l => l.Mod, l => l.Place, "look", problems);
                }
                else if (source.Path.EndsWith(LookReader.SpaceExtension, StringComparison.OrdinalIgnoreCase))
                {
                    var read = new List<SpaceDeclaration>();
                    LookReader.ReadSpaces(source, kinds, read, problems);
                    foreach (var space in read) Put(spaces, space.Name, space, s => s.Mod, s => s.Place, "space", problems);
                }
            }
            // 뒤 모드가 이길 수 있으므로 다 읽은 뒤에 본다
            foreach (var look in looks.Values)
                if (look.Exit != null && !looks.ContainsKey(look.Exit))
                    problems.Add(new UiProblem(look.Place, "exit=\"" + look.Exit + "\": no look is named '" + look.Exit + "'" + UiSuggest.Hint(look.Exit, looks.Keys)));
            return new LookLibrary(looks, spaces, problems);
        }

        private static void CheckFiles(IReadOnlyList<LookElement> elements, Func<string, bool> fileExists, List<UiProblem> problems)
        {
            foreach (var element in elements)
            {
                foreach (var attribute in element.Attributes)
                    if (attribute.Value.Type == LookAttributeType.Path && !fileExists(attribute.Value.Text!))
                        problems.Add(new UiProblem(element.Place, attribute.Key + "=\"" + attribute.Value.Text + "\": no such file in the mods"));
                CheckFiles(element.Children, fileExists, problems);
            }
        }

        private static void Put<T>(SortedDictionary<string, T> into, string name, T item, Func<T, string> mod, Func<T, string> place, string what,
            List<UiProblem> problems)
        {
            if (into.TryGetValue(name, out var earlier) && mod(earlier) == mod(item))
            {
                problems.Add(new UiProblem(place(item), "the " + what + " '" + name + "' is already defined at " + place(earlier)));
                return;
            }
            into[name] = item;
        }
    }
}
