using Kesa.Engine.Script;

namespace Kesa.Engine.Loading
{
    /// <summary>④가 한 모드의 식을 평가할 때 쓰는 이름 공간: 그 모드 파일이 묶은 이름과 내장 이름.</summary>
    internal sealed class ModEnvironment
    {
        public ModEnvironment(ModScope scope, IBuiltins builtins)
        {
            Scope = scope;
            Builtins = builtins;
        }

        public ModScope Scope { get; }
        public IBuiltins Builtins { get; }
    }
}
