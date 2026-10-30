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

        /// <summary>월드 물체마다 지난 계산(G401). 영수증이 null이면 다시 계산한다.</summary>
        private readonly SortedDictionary<long, Tracked> _objects = new SortedDictionary<long, Tracked>();

        /// <summary>빠진 엔티티와 빠진 때의 도장.</summary>
        private readonly SortedDictionary<long, long> _gone = new SortedDictionary<long, long>();

        private long _objectStamp;

        /// <summary>표시 함수마다 지난 영수증과 낸 열쇠(G395). 영수증이 null이면 다시 부른다.</summary>
        private readonly SortedDictionary<string, MarkSet> _markSets = new SortedDictionary<string, MarkSet>(StringComparer.Ordinal);

        private readonly SortedDictionary<string, ShownMark> _marks = new SortedDictionary<string, ShownMark>(StringComparer.Ordinal);

        /// <summary>없어진 표시의 열쇠와 없어진 때의 도장.</summary>
        private readonly SortedDictionary<string, long> _marksGone = new SortedDictionary<string, long>(StringComparer.Ordinal);

        private long _markStamp;

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

        public ValueTask<BridgeWorldObjects> WorldObjects(long since)
        {
            var view = View;
            var ids = new SortedSet<long>();
            foreach (var id in view.WorldEntities()) ids.Add(id.Value);
            var left = new List<long>();
            foreach (var id in _objects.Keys)
                if (!ids.Contains(id)) left.Add(id);
            foreach (var id in left)
            {
                if (_objects[id].Shown != null) _gone[id] = ++_objectStamp;
                _objects.Remove(id);
            }
            foreach (var id in ids)
            {
                if (_objects.TryGetValue(id, out var old) && old.Receipt != null && !old.Receipt.Changed(World)) continue;
                var shown = Compute(view, id, out var receipt);
                var before = old?.Shown;
                long stamp = old?.Stamp ?? 0;
                if (!Equals(shown, before))
                {
                    stamp = ++_objectStamp;
                    if (shown == null) _gone[id] = stamp;
                    else _gone.Remove(id);
                }
                _objects[id] = new Tracked(receipt, shown, stamp);
            }
            var changed = new List<BridgeWorldObject>();
            foreach (var pair in _objects)
                if (pair.Value.Shown != null && pair.Value.Stamp > since) changed.Add(pair.Value.Shown);
            var gone = new List<long>();
            foreach (var pair in _gone)
                if (since > 0 && pair.Value > since) gone.Add(pair.Key);
            return Done(new BridgeWorldObjects(_objectStamp, changed, gone));
        }

        public ValueTask<BridgeWorldObject?> WorldObject(string key, long entity)
        {
            var shown = Compute(View, entity, out var receipt);
            _receipts[key] = receipt;
            return Done(shown);
        }

        /// <summary>한 엔티티의 월드 물체. 그리지 않으면 null이고, 칸 함수가 실패하면 영수증 없이 오류를 싣는다.</summary>
        private BridgeWorldObject? Compute(WorldView view, long id, out CallResult? receipt)
        {
            var entity = new EntityId(id);
            try
            {
                receipt = view.WorldObject(_ui, entity);
                if (!(receipt.Value is ListValue pair)) return null;
                var defOf = DefOf(view);
                return new BridgeWorldObject(new BridgeEntity(id, defOf(entity)), KernelValues.ToBridge(pair.Items[0], defOf).AsLook,
                    KernelValues.ToBridge(pair.Items[1], defOf).AsWhere, null);
            }
            catch (Exception e) when (e is WorldException || e is BridgeValueException)
            {
                receipt = null;
                return new BridgeWorldObject(new BridgeEntity(id, World.IsAlive(entity) ? World.DefOf(entity) : null), null, null, e.Message);
            }
        }

        public ValueTask<BridgeWorldMarks> WorldMarks(long since)
        {
            var view = View;
            foreach (var function in World.WorldMarkFunctions)
            {
                _markSets.TryGetValue(function, out var old);
                if (old != null && old.Receipt != null && !old.Receipt.Changed(World)) continue;
                var marks = ComputeMarks(view, function, out var receipt);
                if (old != null)
                    foreach (var key in old.Keys)
                        if (!marks.ContainsKey(key))
                        {
                            _marks.Remove(key);
                            _marksGone[key] = ++_markStamp;
                        }
                foreach (var pair in marks)
                {
                    if (_marks.TryGetValue(pair.Key, out var was) && Equals(was.Mark, pair.Value)) continue;
                    _marks[pair.Key] = new ShownMark(pair.Value, ++_markStamp);
                    _marksGone.Remove(pair.Key);
                }
                _markSets[function] = new MarkSet(receipt, new List<string>(marks.Keys));
            }
            var changed = new List<BridgeMark>();
            foreach (var pair in _marks)
                if (pair.Value.Stamp > since) changed.Add(pair.Value.Mark);
            var gone = new List<string>();
            foreach (var pair in _marksGone)
                if (pair.Value > since) gone.Add(pair.Key);
            return Done(new BridgeWorldMarks(_markStamp, changed, gone));
        }

        /// <summary>
        /// 한 표시 함수의 표시를 열쇠로 (G396): <c>함수id/</c> 뒤에 <c>key=</c>가 있으면 그 값, 없으면 그림 이름과 자리, 겹치면 <c>#순번</c>.
        /// 함수가 실패하면 영수증 없이 오류 표시 하나다.
        /// </summary>
        private SortedDictionary<string, BridgeMark> ComputeMarks(WorldView view, string function, out CallResult? receipt)
        {
            var marks = new SortedDictionary<string, BridgeMark>(StringComparer.Ordinal);
            try
            {
                receipt = view.WorldMarks(_ui, function);
                var defOf = DefOf(view);
                foreach (var item in ((ListValue)receipt.Value).Items)
                {
                    var mark = (MarkValue)item;
                    var look = KernelValues.ToBridge(mark.Look, defOf).AsLook;
                    var where = KernelValues.ToBridge(mark.Where, defOf).AsWhere;
                    BridgeMarkClick? click = null;
                    if (mark.Action != null)
                    {
                        var arguments = new SortedDictionary<string, BridgeValue>(StringComparer.Ordinal);
                        foreach (var pair in mark.Arguments!) arguments[pair.Key] = KernelValues.ToBridge(pair.Value, defOf);
                        click = new BridgeMarkClick(mark.Action, arguments);
                    }
                    string key = function + "/" + (mark.Key != null
                        ? "key=" + BridgeJson.Write(KernelValues.ToBridge(mark.Key, defOf))
                        : look.Name + "@" + BridgeJson.Write(where));
                    string unique = key;
                    for (int n = 1; marks.ContainsKey(unique); n++) unique = key + "#" + n.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    marks.Add(unique, new BridgeMark(unique, look, where, click, null));
                }
            }
            catch (Exception e) when (e is WorldException || e is BridgeValueException)
            {
                receipt = null;
                marks.Clear();
                marks.Add(function, new BridgeMark(function, null, null, null, e.Message));
            }
            return marks;
        }

        private sealed class MarkSet
        {
            public MarkSet(CallResult? receipt, IReadOnlyList<string> keys)
            {
                Receipt = receipt;
                Keys = keys;
            }

            public CallResult? Receipt { get; }
            public IReadOnlyList<string> Keys { get; }
        }

        private sealed class ShownMark
        {
            public ShownMark(BridgeMark mark, long stamp)
            {
                Mark = mark;
                Stamp = stamp;
            }

            public BridgeMark Mark { get; }
            public long Stamp { get; }
        }

        private sealed class Tracked
        {
            public Tracked(CallResult? receipt, BridgeWorldObject? shown, long stamp)
            {
                Receipt = receipt;
                Shown = shown;
                Stamp = stamp;
            }

            public CallResult? Receipt { get; }
            public BridgeWorldObject? Shown { get; }
            public long Stamp { get; }
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

        public ValueTask<IReadOnlyList<BridgeRequest>> UndrawnRequests()
        {
            var view = View;
            var defOf = DefOf(view);
            var requests = new List<BridgeRequest>();
            foreach (var q in view.OpenRequests)
            {
                bool drawn = false;
                foreach (var receipt in _receipts.Values)
                    if (receipt != null && receipt.ReadsQuestion(q.Name)) drawn = true;
                if (!drawn) requests.Add(KernelResults.Request(q, defOf));
            }
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
                if (_host.Loaded.UiActions.ContainsKey(name))
                    return Done(KernelResults.Check(view.CheckPreconditions(_ui, name, args), DefOf(view)));
                // 선택지 답은 커맨드가 아니라 사전 조건이 없다: 낼 수 있는 질문이 있는가만 본다 (G379)
                if (name == World.PickCommand)
                {
                    string? whyNot = view.WhyNotDeliverable(name, args);
                    return Done(whyNot == null ? KernelResults.Passed() : KernelResults.Denied(whyNot));
                }

                var report = view.CheckPreconditions(name, args);
                // 인자가 맞으면 입구를 본다: 이 연결이 낼 수 있는가가 사전 조건보다 먼저 이유다 (G233·G283)
                if (report.Arguments.Count == 0)
                {
                    string? why = view.WhyNotDeliverable(name, args);
                    if (why != null) return Done(KernelResults.Denied(why));
                }
                return Done(KernelResults.Check(report, DefOf(view)));
            }
            catch (WorldException e)
            {
                return Done(KernelResults.Failure(e));
            }
        }

        public ValueTask<BridgeResult> Deliver(string command, IReadOnlyDictionary<string, BridgeValue> arguments)
        {
            var args = KernelValues.ToKernel(arguments);
            var delivered = World.Deliver(Connection, new UiSubmission(command, args));
            var view = View;
            return Done(KernelResults.Result(view.Show(delivered), DefOf(view)));
        }

        public ValueTask<long> Version() => Done(World.Version());

        public ValueTask<long> UiVersion() => Done(_ui.Version);

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
            var view = View;
            return Done(Delivered(view, view.Show(view.Act(_ui, action, KernelValues.ToKernel(arguments)))));
        }

        public ValueTask<BridgeResult?> WorldInput(string input, IReadOnlyList<long> entities, BridgeWhere? place = null)
        {
            var ids = new List<EntityId>(entities.Count);
            foreach (var id in entities) ids.Add(new EntityId(id));
            bool at = place != null && place.Shape == BridgeWhereShape.At && place.Name != null && place.Target != null;
            var view = View;
            var acted = view.WorldInput(_ui, input, ids, at ? place!.Name : null, at ? KernelValues.ToKernel(place!.Target!) : null);
            return Done(acted == null ? null : Delivered(view, view.Show(acted)));
        }

        private BridgeResult Delivered(WorldView view, ActResult acted)
        {
            if (!acted.Succeeded) return KernelResults.Result(acted, DefOf(view));
            var deliveries = new List<BridgeDelivery>(acted.Submissions.Count);
            foreach (var submission in acted.Submissions)
            {
                var delivered = World.Deliver(Connection, submission);
                var after = View;
                deliveries.Add(new BridgeDelivery(submission.Command, KernelResults.Arguments(submission.Arguments, DefOf(view)),
                    KernelResults.Result(after.Show(delivered), DefOf(after))));
            }
            return new BridgeResult(true, acted.Outcome.ToString(), null, deliveries);
        }

        public ValueTask<IReadOnlyList<BridgeBot>> Bots()
        {
            var view = View;
            var bots = new List<BridgeBot>();
            foreach (var pair in _host.Loaded.Bots)
            {
                var settings = new List<BridgeParameter>();
                foreach (var p in World.BotSettings(pair.Key))
                    settings.Add(new BridgeParameter(p.Name, p.Type.Display, p.Default == null ? null : KernelValues.ToBridge(p.Default, DefOf(view))));
                bots.Add(new BridgeBot(pair.Key, pair.Value.Parameters[0].Type.Display, settings));
            }
            return Done<IReadOnlyList<BridgeBot>>(bots);
        }

        public ValueTask<IScriptBot> Bot(string bot, long entity, IReadOnlyDictionary<string, BridgeValue>? settings = null, long? seed = null,
            int? stepBudget = null)
        {
            BotState state;
            try
            {
                state = World.NewBot(bot, new EntityId(entity), settings == null ? null : KernelValues.ToKernel(settings), seed, stepBudget);
            }
            catch (WorldException e)
            {
                throw new ArgumentException(e.Hint == null ? e.Message : e.Message + " (" + e.Hint + ")", e);
            }
            return Done<IScriptBot>(new LocalScriptBot(_host, Connection, state));
        }

        public ValueTask<IScriptBot> LoadBot(byte[] saved, int? stepBudget = null)
        {
            BotState state;
            IReadOnlyList<string> warnings;
            try
            {
                state = World.LoadBot(saved, out warnings, stepBudget);
            }
            catch (WorldException e)
            {
                throw new ArgumentException(e.Message, e);
            }
            var problems = new List<BridgeReason>();
            foreach (var w in warnings) problems.Add(new BridgeReason(BridgeReasonKind.Error, message: w, code: "bot-load"));
            return Done<IScriptBot>(new LocalScriptBot(_host, Connection, state, problems));
        }

        /// <summary>보는 쪽에서 엔티티의 Def id. 이미 없거나 못 보는 엔티티면 null.</summary>
        private static Func<EntityId, string?> DefOf(WorldView view) => id => view.IsAlive(id) ? view.DefOf(id) : null;

        private static ValueTask<T> Done<T>(T value) => new ValueTask<T>(value);
    }
}
