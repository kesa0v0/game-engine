using System.Collections.Generic;
using System.Linq;

namespace Kesa.Engine.Ui
{
    public enum InputModifier
    {
        Shift,
        Ctrl,
        Alt,
    }

    /// <summary>
    /// <c>key="SHIFT + R"</c> 하나 (G423). <see cref="Key"/>는 호스트가 준 이름 목록의 철자이거나 <see cref="LookBoard.Primary"/>·
    /// <see cref="LookBoard.Secondary"/>다.
    /// </summary>
    public sealed class InputChord
    {
        public InputChord(IReadOnlyList<InputModifier> modifiers, string key)
        {
            Modifiers = modifiers;
            Key = key;
        }

        /// <summary>적힌 순서와 상관없이 <see cref="InputModifier"/> 순서다.</summary>
        public IReadOnlyList<InputModifier> Modifiers { get; }

        public string Key { get; }

        public bool IsPointer => Key == LookBoard.Primary || Key == LookBoard.Secondary;

        /// <summary>같은 키에 수정 키를 더 붙인 <paramref name="other"/>가 같이 눌렸으면 이것은 내지 않는다(<c>SHIFT + R</c>이면 <c>R</c>은 빠짐, G423).</summary>
        public bool IsCoveredBy(InputChord other) =>
            other.Key == Key && other.Modifiers.Count > Modifiers.Count && Modifiers.All(other.Modifiers.Contains);

        public override string ToString() => string.Join(" + ", Modifiers.Select(m => m.ToString().ToUpperInvariant()).Append(Key));
    }

    /// <summary><c>&lt;Input name="…" key="…"/&gt;</c> 하나 (G417).</summary>
    public sealed class InputDeclaration
    {
        public InputDeclaration(string name, string mod, string place, InputChord key, string? kind = null, GestureShape? shape = null)
        {
            Name = name;
            Mod = mod;
            Place = place;
            Key = key;
            Kind = kind;
            Shape = shape;
        }

        public string Name { get; }
        public string Mod { get; }
        public string Place { get; }
        public InputChord Key { get; }

        /// <summary><c>kind=</c>의 이름 (G489). 없으면 누름 입력이다.</summary>
        public string? Kind { get; }

        /// <summary>그 모양. 모르는 종류를 받아 주는 도구에서는 <see cref="Kind"/>가 있어도 null이다.</summary>
        public GestureShape? Shape { get; }
    }
}
