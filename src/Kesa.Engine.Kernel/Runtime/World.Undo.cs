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
        private List<Action>? _undo;

        /// <summary>
        /// <paramref name="run"/>을 되돌릴 수 있게 실행한다. 어떤 예외로 끝나든(스크립트 오류, 엔진 오류) 그동안의 변경을 거꾸로
        /// 되감은 뒤 예외를 그대로 던진다.
        /// </summary>
        private T Atomically<T>(Func<T> run)
        {
            if (_undo != null) throw new InvalidOperationException("a command is already running");
            var undo = new List<Action>();
            _undo = undo;
            try
            {
                return run();
            }
            catch
            {
                _undo = null; // 되감는 동작은 기록하지 않는다
                for (int i = undo.Count - 1; i >= 0; i--) undo[i]();
                throw;
            }
            finally
            {
                _undo = null;
            }
        }

        private void Journal(Action undo) => _undo?.Add(undo);

        /// <summary>값 칸 하나에 쓴다. 커맨드 실행 중이면 원래 값(또는 "값 없음")을 기록한다.</summary>
        private void Assign(Dictionary<MemberDecl, Value> values, MemberDecl member, Value value)
        {
            if (_undo != null)
            {
                if (values.TryGetValue(member, out var old)) Journal(() => values[member] = old);
                else Journal(() => values.Remove(member));
            }
            values[member] = value;
        }
    }
}
