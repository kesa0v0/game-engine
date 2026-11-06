using System;
using Kesa.Engine.Runtime;

namespace Kesa.Engine.Host
{
    /// <summary>창구가 월드를 부르는 동안 공개 쓰기 막기를 푼다. 호스트 함수의 읽기·쓰기마다 지나므로 구조체다.</summary>
    internal readonly struct EngineScope : IDisposable
    {
        private readonly World _world;
        private readonly int _saved;

        internal EngineScope(World world)
        {
            _world = world;
            _saved = world.EnterEngine();
        }

        public void Dispose() => _world.LeaveEngine(_saved);
    }
}
