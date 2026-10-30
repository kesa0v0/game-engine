#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Kesa.Engine.Ui;
using UnityEngine.InputSystem;

namespace Kesa.Engine.Unity
{
    /// <summary>
    /// 모드 입력 선언을 Input System 액션으로 (G417·G423). 내장 <c>primary</c>·<c>secondary</c>도 액션이다. 플레이어가 키를 바꾸는 것은
    /// <see cref="Actions"/>에 Input System의 다시 묶기·<c>SaveBindingOverridesAsJson</c>을 쓴다.
    /// </summary>
    public sealed class ModInputs : IDisposable
    {
        private readonly List<(InputAction Action, InputChord Chord)> _inputs = new List<(InputAction, InputChord)>();

        public ModInputs(InputLibrary library)
        {
            Actions = new InputActionMap("Kesa world inputs");
            Add(LookBoard.Primary, new InputChord(Array.Empty<InputModifier>(), LookBoard.Primary));
            Add(LookBoard.Secondary, new InputChord(Array.Empty<InputModifier>(), LookBoard.Secondary));
            foreach (var input in library.Inputs) Add(input.Key, input.Value.Key);
            Actions.Enable();
        }

        /// <summary><c>.input.xml</c>에 적을 수 있는 키 이름: Input System <c>Key</c> (G423).</summary>
        public static IReadOnlyList<string> KeyNames { get; } =
            Enum.GetNames(typeof(Key)).Where(n => n != nameof(Key.None) && n != "IMESelected").ToList();

        public InputActionMap Actions { get; }

        /// <summary>
        /// 이번 프레임에 눌린 입력 이름. 같은 키에 수정 키가 더 붙은 입력이 같이 눌렸으면 그것만 낸다(<c>SHIFT + R</c>이면 <c>R</c>은 빠짐).
        /// </summary>
        public IReadOnlyList<string> PressedThisFrame()
        {
            var pressed = _inputs.Where(i => i.Action.WasPressedThisFrame()).ToList();
            return pressed
                .Where(i => !pressed.Any(other => i.Chord.IsCoveredBy(other.Chord)))
                .Select(i => i.Action.name).ToList();
        }

        /// <summary>키 하나의 Input System 경로. <c>Digit1</c>은 <c>&lt;Keyboard&gt;/1</c>이다.</summary>
        public static string PathOf(string key)
        {
            if (key == LookBoard.Primary) return "<Pointer>/press";
            if (key == LookBoard.Secondary) return "<Mouse>/rightButton";
            string control = key.StartsWith("Digit", StringComparison.Ordinal) && key.Length == 6 ? key.Substring(5)
                : char.ToLowerInvariant(key[0]) + key.Substring(1);
            return "<Keyboard>/" + control;
        }

        private void Add(string name, InputChord chord)
        {
            var action = Actions.AddAction(name, InputActionType.Button);
            var modifiers = chord.Modifiers.Select(ModifierPath).ToList();
            switch (modifiers.Count)
            {
                case 0:
                    action.AddBinding(PathOf(chord.Key));
                    break;
                case 1:
                    action.AddCompositeBinding("OneModifier").With("Modifier", modifiers[0]).With("Binding", PathOf(chord.Key));
                    break;
                default:
                    action.AddCompositeBinding("TwoModifiers").With("Modifier1", modifiers[0]).With("Modifier2", modifiers[1])
                        .With("Binding", PathOf(chord.Key));
                    break;
            }
            _inputs.Add((action, chord));
        }

        private static string ModifierPath(InputModifier modifier) => modifier switch
        {
            InputModifier.Shift => "<Keyboard>/shift",
            InputModifier.Ctrl => "<Keyboard>/ctrl",
            _ => "<Keyboard>/alt",
        };

        public void Dispose()
        {
            Actions.Disable();
            Actions.Dispose();
        }
    }
}
