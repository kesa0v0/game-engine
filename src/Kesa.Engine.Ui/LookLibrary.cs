using System;
using System.Collections.Generic;
using Kesa.Engine.Bridge;

namespace Kesa.Engine.Ui
{
    /// <summary>
    /// 모드들의 그림 정의, 공간 선언, 보는 법을 로드 순서대로 모은 것 (G390·G392·G409~G411·G441). 같은 이름은 뒤 모드가 통째로 이기고, 한 모드 안의
    /// 같은 이름은 문제다. 에셋은 읽지 않는다(G392: 처음 보일 때 렌더러가 읽음).
    /// </summary>
    public sealed class LookLibrary
    {
        private LookLibrary(IReadOnlyDictionary<string, LookDefinition> looks, IReadOnlyDictionary<string, SpaceDeclaration> spaces,
            IReadOnlyDictionary<string, ViewDeclaration> views, IReadOnlyList<UiProblem> problems)
        {
            Looks = looks;
            Spaces = spaces;
            Views = views;
            Problems = problems;
        }

        public IReadOnlyDictionary<string, LookDefinition> Looks { get; }

        public IReadOnlyDictionary<string, SpaceDeclaration> Spaces { get; }

        /// <summary>보는 법(<c>.view.xml</c>, G441). 볼 자리는 이미 공간의 위치로 바꿔 두었다.</summary>
        public IReadOnlyDictionary<string, ViewDeclaration> Views { get; }

        /// <summary>읽다 난 문제. 틀린 정의·선언만 빠지고 나머지는 쓴다.</summary>
        public IReadOnlyList<UiProblem> Problems { get; }

        /// <summary>
        /// <paramref name="sources"/>는 모드의 로드 순서대로다. 확장자가 <c>.look.xml</c>·<c>.space.xml</c>·<c>.view.xml</c>이 아닌 파일은 건너뛴다.
        /// </summary>
        /// <param name="fileExists">적어 둔 그림 경로(G335 가상 경로)가 있는지. 주면 없는 파일을 로드 때 알린다(G392). 묶인 경로는 그릴 때 안다.</param>
        public static LookLibrary Load(IEnumerable<LookSource> sources, LookKinds kinds, Func<string, bool>? fileExists = null)
        {
            var problems = new List<UiProblem>();
            var looks = new SortedDictionary<string, LookDefinition>(StringComparer.Ordinal);
            var spaces = new SortedDictionary<string, SpaceDeclaration>(StringComparer.Ordinal);
            var views = new SortedDictionary<string, ViewDeclaration>(StringComparer.Ordinal);
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
                else if (source.Path.EndsWith(LookReader.ViewExtension, StringComparison.OrdinalIgnoreCase))
                {
                    var read = new List<ViewDeclaration>();
                    LookReader.ReadViews(source, kinds, read, problems);
                    foreach (var view in read) Put(views, view.Name, view, v => v.Mod, v => v.Place, "view", problems);
                }
            }
            // 뒤 모드가 이길 수 있으므로 다 읽은 뒤에 본다
            foreach (var look in looks.Values)
                if (look.Exit != null && !looks.ContainsKey(look.Exit))
                    problems.Add(new UiProblem(look.Place, "exit=\"" + look.Exit + "\": no look is named '" + look.Exit + "'" + UiSuggest.Hint(look.Exit, looks.Keys)));
            var aimed = new SortedDictionary<string, ViewDeclaration>(StringComparer.Ordinal);
            foreach (var view in views.Values)
            {
                string? error = Aim(view, spaces);
                if (error == null) aimed.Add(view.Name, view);
                else problems.Add(new UiProblem(view.Place, error));
            }
            return new LookLibrary(looks, spaces, aimed, problems);
        }

        // 볼 자리를 그 공간의 위치로 바꾸고 offset을 그 자리의 틀로 민다 (G443·G445)
        private static string? Aim(ViewDeclaration view, IReadOnlyDictionary<string, SpaceDeclaration> spaces)
        {
            if (!spaces.TryGetValue(view.Space, out var space))
                return "space=\"" + view.Space + "\": no space is named '" + view.Space + "'" + UiSuggest.Hint(view.Space, spaces.Keys);
            if (!space.Layout.TryPlace(view.LookAt, out var at, out string error)) return "look-at: " + error;
            var frame = space.Layout.FrameAt(at);
            long unit = space.Layout.Unit;
            try
            {
                long Along(long east, long north, long up) => checked(
                    (east * view.Offset[0] / BridgeFixed.Scale + north * view.Offset[1] / BridgeFixed.Scale + up * view.Offset[2] / BridgeFixed.Scale)
                    * unit / BridgeFixed.Scale);
                view.Target = new LookPosition(
                    checked(at.X + Along(frame.East.X, frame.North.X, frame.Up.X)),
                    checked(at.Y + Along(frame.East.Y, frame.North.Y, frame.Up.Y)),
                    checked(at.Z + Along(frame.East.Z, frame.North.Z, frame.Up.Z)));
            }
            catch (OverflowException)
            {
                return "offset: the place is too far away";
            }
            view.Frame = frame;
            view.Unit = unit;
            return null;
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
