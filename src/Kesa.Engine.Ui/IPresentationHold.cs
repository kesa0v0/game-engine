using System.Threading.Tasks;

namespace Kesa.Engine.Ui
{
    /// <summary>
    /// <c>staged</c> 연출 (G399·G429·G430): 효과를 단계별로 보이는 동안 새 판과 화면을 붙잡는다. <see cref="UiInterpreter.Hold"/>에 꽂으면
    /// 화면 갱신이 붙잡는 동안 멈추고, 그동안의 누름은 동작을 내지 않고 <see cref="Skip"/>이 된다.
    /// </summary>
    public interface IPresentationHold
    {
        /// <summary>지금 붙잡고 있나.</summary>
        bool Holding { get; }

        /// <summary>새 효과가 왔으면 붙잡기 시작한다. 붙잡고 있으면(이미 또는 새로) 참.</summary>
        ValueTask<bool> HoldAsync();

        /// <summary>남은 재생을 건너뛰고 놓는다.</summary>
        void Skip();
    }
}
