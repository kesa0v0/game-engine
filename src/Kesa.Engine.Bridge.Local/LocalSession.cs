using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Kesa.Engine.Runtime;
using Kesa.Engine.Schema;
using Kesa.Engine.Script;

namespace Kesa.Engine.Bridge.Local
{
    /// <summary>
    /// 연결 세션의 같은 프로세스 구현 (spec-gaps G325). 읽기와 결과는 모두 그 연결의 <see cref="WorldView"/>를 거친다(G201·G202).
    /// UI 기록(G279)과 키마다의 영수증(G246)을 든다.
    /// </summary>
    internal sealed class LocalSession : IHostSession
    {
        private readonly LocalHost _host;
        private readonly UiState _ui;

        /// <summary>키마다의 영수증. 지난 부르기가 실패했으면 null(다시 부를 것).</summary>
        private readonly SortedDictionary<string, CallResult?> _receipts = new SortedDictionary<string, CallResult?>(StringComparer.Ordinal);

        internal LocalSession(LocalHost host, string connection)
        {
            _host = host;
            Connection = connection;
            _ui = host.World.NewUiState();
        }

        public string Connection { get; }

        private World World => _host.World;

        private WorldView View => World.ViewAs(Connection);

        public ValueTask<IReadOnlyList<BridgeEntity>> Seats()
        {
            var ids = new List<EntityId>(World.SeatsOf(Connection));
            ids.Sort((a, b) => a.Value.CompareTo(b.Value));
            var seats = new List<BridgeEntity>(ids.Count);
            foreach (var id in ids) seats.Add(new BridgeEntity(id.Value, World.DefOf(id)));
            return Done<IReadOnlyList<BridgeEntity>>(seats);
        }

        public ValueTask<long> Tick() => Done(World.Tick);

        public ValueTask<IReadOnlyList<BridgeCommand>> Commands() => Done(KernelResults.Commands(_host.Loaded.Commands));

        public ValueTask<IReadOnlyList<BridgeCommand>> UiActions() => Done(KernelResults.Commands(_host.Loaded.UiActions));

        public ValueTask<string?> CheckCall(string function, int argumentCount) =>
            Done(View.CheckCall(function, argumentCount, withUi: true));

        public ValueTask<BridgeCall> Call(string key, string function, IReadOnlyList<BridgeValue> arguments)
        {
            var view = View;
            var args = new Value[arguments.Count];
            for (int i = 0; i < args.Length; i++) args[i] = KernelValues.ToKernel(arguments[i]);
            try
            {
                var result = view.Call(_ui, function, args);
                var value = KernelValues.ToBridge(result.Value, DefOf(view));
                _receipts[key] = result;
                return Done(new BridgeCall(value, null));
            }
            catch (WorldException e)
            {
                _receipts[key] = null;
                return Done(new BridgeCall(null, e.Message));
            }
            catch (BridgeValueException e)
            {
                _receipts[key] = null;
                return Done(new BridgeCall(null, e.Message));
            }
        }

        public ValueTask<IReadOnlyList<string>> Changed()
        {
            var keys = new List<string>();
            foreach (var receipt in _receipts)
                if (receipt.Value == null || receipt.Value.Changed(World)) keys.Add(receipt.Key);
            return Done<IReadOnlyList<string>>(keys);
        }

        public ValueTask Unbind(string key)
        {
            _receipts.Remove(key);
            return default;
        }

        public ValueTask<IReadOnlyList<BridgeListed>> Entities()
        {
            var view = View;
            var listed = new List<BridgeListed>();
            foreach (var type in _host.Loaded.Schema.Types)
            {
                foreach (var id in view.All(type.Name))
                    listed.Add(new BridgeListed(new BridgeEntity(id.Value, view.DefOf(id)), World.Version(id), HasDerived(view, id)));
            }
            return Done<IReadOnlyList<BridgeListed>>(listed);
        }

        public ValueTask<IReadOnlyList<BridgeComponent>?> Read(long entity)
        {
            var view = View;
            var id = new EntityId(entity);
            if (!view.Sees(id)) return Done<IReadOnlyList<BridgeComponent>?>(null);
            var defOf = DefOf(view);
            var components = new List<BridgeComponent>();
            foreach (var component in _host.Loaded.Schema.Components)
            {
                if (!view.Has(id, component.Name)) continue;
                var members = new List<KeyValuePair<string, BridgeValue>>(component.Members.Count);
                foreach (var m in component.Members)
                {
                    var value = KernelValues.ToBridge(view.Get(id, World.Member(component.Name, m.Name)), defOf);
                    members.Add(new KeyValuePair<string, BridgeValue>(m.Name, value));
                }
                components.Add(new BridgeComponent(component.Name, members));
            }
            return Done<IReadOnlyList<BridgeComponent>?>(components);
        }

        private bool HasDerived(WorldView view, EntityId id)
        {
            foreach (var component in _host.Loaded.Schema.Components)
            {
                if (!view.Has(id, component.Name)) continue;
                foreach (var m in component.Members)
                    if (m.Kind == MemberKind.Derived) return true;
            }
            return false;
        }

        public ValueTask<IReadOnlyList<BridgeRequest>> OpenRequests()
        {
            var view = View;
            var defOf = DefOf(view);
            var requests = new List<BridgeRequest>();
            foreach (var q in view.OpenRequests) requests.Add(KernelResults.Request(q, defOf));
            return Done<IReadOnlyList<BridgeRequest>>(requests);
        }

        public ValueTask<BridgeMoves> LegalResponses(long request, int max)
        {
            var view = View;
            try
            {
                return Done(KernelResults.Moves(view.LegalResponses(request, null, max), DefOf(view)));
            }
            catch (WorldException e)
            {
                return Done(KernelResults.NoMoves(e));
            }
        }

        public ValueTask<BridgeMoves> LegalMoves(string command, IReadOnlyDictionary<string, BridgeValue> given, int max)
        {
            var view = View;
            try
            {
                return Done(KernelResults.Moves(view.LegalMoves(command, KernelValues.ToKernel(given), max), DefOf(view)));
            }
            catch (WorldException e)
            {
                return Done(KernelResults.NoMoves(e));
            }
        }

        public ValueTask<BridgeMoves> LegalUiMoves(string action, IReadOnlyDictionary<string, BridgeValue> given, int max)
        {
            var view = View;
            try
            {
                return Done(KernelResults.Moves(view.LegalMoves(_ui, action, KernelValues.ToKernel(given), max), DefOf(view)));
            }
            catch (WorldException e)
            {
                return Done(KernelResults.NoMoves(e));
            }
        }

        public ValueTask<BridgeResult> Check(string name, IReadOnlyDictionary<string, BridgeValue> arguments)
        {
            var args = KernelValues.ToKernel(arguments);
            var view = View;
            try
            {
                var report = _host.Loaded.UiActions.ContainsKey(name)
                    ? view.CheckPreconditions(_ui, name, args)
                    : view.CheckPreconditions(name, args);
                return Done(KernelResults.Check(report, DefOf(view)));
            }
            catch (WorldException e)
            {
                return Done(KernelResults.Failure(e));
            }
        }

        public ValueTask<long> Version() => Done(World.Version());

        public ValueTask<BridgeResult> Submit(string command, IReadOnlyDictionary<string, BridgeValue> arguments)
        {
            var args = KernelValues.ToKernel(arguments);
            var view = View;
            return Done(KernelResults.Result(view.Show(World.Submit(Connection, command, args)), DefOf(view)));
        }

        public ValueTask<BridgeResult> Respond(long request, string command, IReadOnlyDictionary<string, BridgeValue> arguments)
        {
            var args = KernelValues.ToKernel(arguments);
            var view = View;
            return Done(KernelResults.Result(view.Show(World.Respond(Connection, request, command, args)), DefOf(view)));
        }

        /// <summary>화면 동작을 하고 적힌 <c>submit</c>을 차례로 낸다. 하나가 실패해도 나머지는 낸다(콘솔 호스트와 같다, G283).</summary>
        public ValueTask<BridgeResult> Act(string action, IReadOnlyDictionary<string, BridgeValue> arguments)
        {
            var args = KernelValues.ToKernel(arguments);
            var view = View;
            var acted = view.Show(view.Act(_ui, action, args));
            if (!acted.Succeeded) return Done(KernelResults.Result(acted, DefOf(view)));
            var deliveries = new List<BridgeDelivery>(acted.Submissions.Count);
            foreach (var submission in acted.Submissions)
            {
                var delivered = World.Deliver(Connection, submission);
                var after = View;
                deliveries.Add(new BridgeDelivery(submission.Command, KernelResults.Arguments(submission.Arguments, DefOf(view)),
                    KernelResults.Result(after.Show(delivered), DefOf(after))));
            }
            return Done(new BridgeResult(true, acted.Outcome.ToString(), null, deliveries));
        }

        /// <summary>보는 쪽에서 엔티티의 Def id. 이미 없거나 못 보는 엔티티면 null.</summary>
        private static Func<EntityId, string?> DefOf(WorldView view) => id => view.IsAlive(id) ? view.DefOf(id) : null;

        private static ValueTask<T> Done<T>(T value) => new ValueTask<T>(value);
    }
}
