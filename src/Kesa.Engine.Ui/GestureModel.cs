using System.Collections.Generic;
using Kesa.Engine.Bridge;

namespace Kesa.Engine.Ui
{
    /// <summary>
    /// 모양 종류 하나 (G482·G489): <c>&lt;Input kind="…"/&gt;</c>의 이름, 받는 설정, 설정으로 모양을 만드는 일. 공간 종류(<see cref="SpaceKind"/>)처럼
    /// 기본 종류도 게임의 종류도 <see cref="LookKinds.Add(GestureKind)"/>로 등록한다.
    /// </summary>
    public abstract class GestureKind
    {
        public abstract string Name { get; }

        /// <summary>받는 설정 이름(진단의 "혹시 …?"에 쓴다).</summary>
        public abstract IReadOnlyCollection<string> Settings { get; }

        /// <summary>설정 글로 모양을 만든다. 틀린 설정은 <paramref name="problems"/>에 담고 null.</summary>
        public abstract GestureShape? Configure(IReadOnlyDictionary<string, string> settings, List<string> problems);
    }

    /// <summary>설정까지 정한 모양. 끌기 하나마다 <see cref="Begin"/>으로 새 획을 연다.</summary>
    public abstract class GestureShape
    {
        /// <summary>
        /// <paramref name="space"/>의 <paramref name="start"/>에서 시작하는 획. 이 공간에서 쓸 수 없는 모양이면(<c>lasso</c>가 칸 없는 공간 등)
        /// 모더가 읽을 이유와 함께 null.
        /// </summary>
        public abstract GestureStroke? Begin(SpaceLayout space, BridgeValue start, out string error);
    }

    /// <summary>진행 중인 획 하나. 핸들러가 받을 값과 <c>drag("입력")</c>이 읽는 값은 같은 <see cref="Value"/>다 (G487).</summary>
    public abstract class GestureStroke
    {
        /// <summary>커서가 시작한 공간의 새 자리로 왔다. 값이 바뀌었으면 참.</summary>
        public abstract bool Add(BridgeValue place);

        /// <summary>지금까지의 값 (G483: 핸들러의 매개변수 하나).</summary>
        public abstract BridgeValue Value { get; }
    }
}
