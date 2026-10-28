using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Kesa.Engine.Runtime;

namespace Kesa.Engine.Bridge.Local
{
    /// <summary>세션이 든 모드 봇 (G378): 커널의 <see cref="BotState"/>와 그 연결.</summary>
    internal sealed class LocalScriptBot : IScriptBot
    {
        private readonly LocalHost _host;
        private readonly string _connection;
        private readonly BotState _state;

        internal LocalScriptBot(LocalHost host, string connection, BotState state, IReadOnlyList<BridgeReason>? problems = null)
        {
            _host = host;
            _connection = connection;
            _state = state;
            LastProblems = problems ?? Array.Empty<BridgeReason>();
        }

        public string Bot => _state.Bot;
        public long Entity => _state.Me.Value;
        public IReadOnlyList<BridgeReason> LastProblems { get; private set; }

        public ValueTask<byte[]> Save() => new ValueTask<byte[]>(_state.Save());

        /// <summary>한 번 생각하고 적은 수를 차례로 낸다. 하나가 거절돼도 나머지는 낸다(화면 동작과 같다, G283).</summary>
        public ValueTask<bool> Act(IHostSession me)
        {
            if (me.Connection != _connection)
                throw new ArgumentException("this bot was made by the session of '" + _connection + "', not '" + me.Connection + "'", nameof(me));
            var world = _host.World;
            var problems = new List<BridgeReason>();
            var thought = world.ViewAs(_connection).Think(_state);
            if (thought.Error != null)
                problems.Add(new BridgeReason(BridgeReasonKind.Error, message: thought.Error.Message, code: thought.Error.Code));
            foreach (var a in thought.ArgumentRejections)
                problems.Add(new BridgeReason(BridgeReasonKind.Argument, message: a.Message, parameter: a.Parameter, code: a.Problem.ToString()));
            bool any = false;
            foreach (var submission in thought.Submissions)
            {
                var delivered = world.Deliver(_connection, submission);
                var view = world.ViewAs(_connection);
                Func<EntityId, string?> defOf = id => view.IsAlive(id) ? view.DefOf(id) : null;
                var result = KernelResults.Result(view.Show(delivered), defOf);
                if (result.Succeeded) any = true;
                else problems.AddRange(result.Reasons);
            }
            LastProblems = problems;
            return new ValueTask<bool>(any);
        }
    }
}
