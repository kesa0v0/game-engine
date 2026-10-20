namespace Kesa.Engine.Script
{
    /// <summary>
    /// 이름 자리의 풀이 (<see cref="NameExpr.Cache"/>): 어느 칸 배치에서 지역 칸 몇 번인가, 아니면(<see cref="Slot"/> -1) 어느 실행기·모드에서
    /// 찾은 전역·내장 값인가.
    /// </summary>
    internal sealed class NameSite
    {
        public NameSite(FrameLayout layout, int slot, PureEvaluator? owner, string? mod, Value? value)
        {
            Layout = layout;
            Slot = slot;
            Owner = owner;
            Mod = mod;
            Value = value;
        }

        public FrameLayout Layout { get; }
        public int Slot { get; }
        public PureEvaluator? Owner { get; }
        public string? Mod { get; }
        public Value? Value { get; }
    }
}
