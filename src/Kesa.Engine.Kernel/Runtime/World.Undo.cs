using System;
using System.Collections.Generic;
using Kesa.Engine.Schema;
using Kesa.Engine.Script;

namespace Kesa.Engine.Runtime
{
    /// <summary>
    /// 커맨드 단위 롤백 (spec C8, spec-gaps G133): 커맨드 실행 중에 바뀌는 칸마다 원래 상태로 돌리는 동작을 기록해 두고,
    /// 실행이 예외로 끝나면 거꾸로 되감는다. 성공하면 기록을 버린다. 비용은 실제로 쓴 횟수에 비례한다.
    /// 상태를 바꾸는 기본 연산(<see cref="Assign"/>, 줄 기록 생성, 역방향 색인)은 모두 여기에 기록한다.
    /// 구조 변경(엔티티 생성·삭제와 id 카운터, 컴포넌트 부착·분리, 줄 붙이기·떼기, 엔티티 키 보유 색인, 기여 행)도
    /// 각 연산이 기록한다 (G134). 연산이 스스로 실패해 정리한 흔적을 되감기가 한 번 더 지워도 같은 상태가 된다.
    /// </summary>
    public sealed partial class World
    {
        /// <summary>커맨드 실행 중의 되돌리기 기록. 커맨드 밖(호스트 API)에서는 null이라 기록하지 않는다.</summary>
        private List<UndoEntry>? _undo;

        /// <summary>
        /// 되돌리기 기록의 그릇. 커맨드마다 새로 만들어 키우지 않고 비워서 다시 쓴다(규모 벤치: 한 틱에 칸 1만 개를 쓰면 목록을 키우는
        /// 복사가 눈에 띄었다). 가장 큰 커맨드만큼의 용량이 남는다.
        /// </summary>
        private readonly List<UndoEntry> _undoBuffer = new List<UndoEntry>();

        /// <summary>
        /// 되돌리기 하나. 가장 흔한 "칸 하나에 쓰기"는 (값 사전, 멤버, 옛 값)으로 적어 람다를 만들지 않고, 그 밖의 연산은 동작을 그대로 든다.
        /// </summary>
        private readonly struct UndoEntry
        {
            private readonly Dictionary<MemberDecl, Value>? _values;
            private readonly MemberDecl? _member;
            private readonly Value? _old;
            private readonly Action? _action;

            private UndoEntry(Dictionary<MemberDecl, Value>? values, MemberDecl? member, Value? old, Action? action)
            {
                _values = values;
                _member = member;
                _old = old;
                _action = action;
            }

            public static UndoEntry Of(Action action) => new UndoEntry(null, null, null, action);

            /// <summary>칸에 원래 값이 있었으면 그 값으로, 없었으면(<paramref name="old"/>가 null) 지운다.</summary>
            public static UndoEntry Slot(Dictionary<MemberDecl, Value> values, MemberDecl member, Value? old) =>
                new UndoEntry(values, member, old, null);

            public void Run()
            {
                if (_action != null)
                {
                    _action();
                    return;
                }
                if (_old == null) _values!.Remove(_member!);
                else _values![_member!] = _old;
            }
        }

        /// <summary>
        /// <paramref name="run"/>을 되돌릴 수 있게 실행한다. 어떤 예외로 끝나든(스크립트 오류, 엔진 오류) 그동안의 변경을 거꾸로
        /// 되감은 뒤 예외를 그대로 던진다.
        /// </summary>
        private T Atomically<T>(Func<T> run)
        {
            if (_undo != null) throw new InvalidOperationException("a command is already running");
            var undo = _undoBuffer;
            undo.Clear();
            _undo = undo;
            try
            {
                return run();
            }
            catch
            {
                _undo = null; // 되감는 동작은 기록하지 않는다
                for (int i = undo.Count - 1; i >= 0; i--) undo[i].Run();
                throw;
            }
            finally
            {
                _undo = null;
                undo.Clear(); // 붙잡은 값을 놓는다
            }
        }

        private void Journal(Action undo) => _undo?.Add(UndoEntry.Of(undo));

        /// <summary>
        /// <paramref name="owner"/> 엔티티의 값 칸 하나에 쓴다. 커맨드 실행 중이면 원래 값(또는 "값 없음")을 기록한다. 그 엔티티를 읽은
        /// <c>when</c> 조건을 다음 정착에서 다시 보게 표시한다 (S5).
        /// </summary>
        private void Assign(long owner, Dictionary<MemberDecl, Value> values, MemberDecl member, Value value)
        {
            Touch(owner);
            if (_undo != null) _undo.Add(UndoEntry.Slot(values, member, values.TryGetValue(member, out var old) ? old : null));
            values[member] = value;
        }
    }
}
