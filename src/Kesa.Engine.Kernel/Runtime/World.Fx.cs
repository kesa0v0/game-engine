using System;
using System.Collections.Generic;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Script;

namespace Kesa.Engine.Runtime
{
    /// <summary>
    /// 한 번 일어나는 효과 (G398·G426): 신호는 밖으로 나가지 않고, 호스트가 가져갈 때 그 연결의 눈으로 <c>@world_fx</c>를 돌린 결과만 나간다.
    /// 커널은 효과 함수가 받는 신호만 최근 <see cref="FxKept"/>개 든다. 판 상태가 아니라 저장하지 않는다.
    /// </summary>
    public sealed partial class World
    {
        private const int FxKept = 256;

        private sealed class FxEntry
        {
            public FxEntry(long stamp, long batch, SignalValue signal)
            {
                Stamp = stamp;
                Batch = batch;
                Signal = signal;
            }

            public long Stamp { get; }
            public long Batch { get; }
            public SignalValue Signal { get; }
        }

        private IReadOnlyList<WorldFxDecl> _worldFx = Array.Empty<WorldFxDecl>();
        private readonly SortedSet<string> _fxSignals = new SortedSet<string>(StringComparer.Ordinal);
        private readonly List<FxEntry> _fxLog = new List<FxEntry>();
        // 1부터: 처음 받은 도장을 다시 넘긴 호스트를 "처음 묻는 호스트"(0)와 가른다
        private long _fxStamp = 1;
        private long _fxBatch;
        private long _fxForgotten;

        private void SetWorldFx(IReadOnlyList<WorldFxDecl> decls)
        {
            _worldFx = decls;
            foreach (var decl in decls) _fxSignals.Add(decl.Signal.Name);
        }

        // 신호 처리가 끝나 되감긴 신호가 빠진 뒤의 대기열을 적는다: 한 번 비울 때가 커맨드 묶음 하나다
        private void RecordFx()
        {
            if (_fxSignals.Count == 0) return;
            bool opened = false;
            foreach (var e in _emitted)
            {
                if (!_fxSignals.Contains(e.Signal.Signal.Name)) continue;
                if (!opened)
                {
                    _fxBatch++;
                    opened = true;
                }
                _fxLog.Add(new FxEntry(++_fxStamp, _fxBatch, e.Signal));
            }
            int over = _fxLog.Count - FxKept;
            if (over <= 0) return;
            _fxForgotten = _fxLog[over - 1].Stamp;
            _fxLog.RemoveRange(0, over);
        }

        /// <summary>
        /// 도장 <paramref name="since"/> 뒤에 난 신호마다 그 신호의 <c>@world_fx</c> 함수를 로드 순서대로 돌린다. 처음(<c>since</c>가 0 이하)은
        /// 지난 효과 없이 지금 도장만 준다.
        /// </summary>
        internal FxResult WorldFxAs(WorldView viewer, UiState ui, long since)
        {
            NoHostWrites("WorldFx");
            var batches = new List<FxBatch>();
            if (since <= 0) return new FxResult(_fxStamp, false, batches);
            List<FxItem>? items = null;
            long batch = 0;
            foreach (var entry in _fxLog)
            {
                if (entry.Stamp <= since) continue;
                if (items == null || entry.Batch != batch)
                {
                    if (items != null && items.Count > 0) batches.Add(new FxBatch(batch, items));
                    items = new List<FxItem>();
                    batch = entry.Batch;
                }
                foreach (var decl in _worldFx)
                    if (decl.Signal.Name == entry.Signal.Signal.Name) RunFx(viewer, ui, decl, entry.Signal, items);
            }
            if (items != null && items.Count > 0) batches.Add(new FxBatch(batch, items));
            return new FxResult(_fxStamp, since < _fxForgotten, batches);
        }

        private void RunFx(WorldView viewer, UiState ui, WorldFxDecl decl, SignalValue signal, List<FxItem> into)
        {
            string id = decl.Id;
            try
            {
                var result = ViewingWithUi(viewer, ui, () => Traced(id, _ =>
                {
                    var args = new List<Value>();
                    foreach (var p in decl.Function.Definition!.Parameters) args.Add(ToScript(signal.Value(p.Name)));
                    var value = RunGateFunction(decl.Function, args, id);
                    if (value is NoneValue) return new ListValue(new List<Value>());
                    if (value is ListValue list)
                    {
                        foreach (var item in list.Items)
                            if (!(item is FxValue))
                                throw new WorldException(Codes.RunType, "'" + id + "' returned a list holding " + item.KindName +
                                                                        ", but a @world_fx function gives a list of fx(...) (spec-gaps G398)");
                        return list;
                    }
                    throw new WorldException(Codes.RunType, "'" + id + "' returned " + value.KindName +
                                                            ", but a @world_fx function gives a list of fx(...) (spec-gaps G398)");
                }));
                foreach (var item in ((ListValue)result.Value).Items) into.Add(new FxItem(id, (FxValue)item, null));
            }
            catch (WorldException e)
            {
                into.Add(new FxItem(id, null, e.Message));
            }
        }

        private Value FxBuiltin(IReadOnlyList<Value> positional, IReadOnlyDictionary<string, Value> keywords)
        {
            ScreenOnly("fx()");
            const string usage = "write fx(look(\"damage-number\", amount=4), at=at(\"square\", [x, y])) (spec-gaps G398)";
            if (positional.Count != 1) throw LookError("fx() takes look(...) or the name of a look, got " + positional.Count + " arguments", usage);
            var look = positional[0] switch
            {
                LookValue l => l,
                StrValue s when s.Value.Length > 0 => new LookValue(s.Value, new SortedDictionary<string, Value>(StringComparer.Ordinal)),
                _ => throw LookError("fx() takes look(...) or the name of a look, not " + positional[0].KindName, usage),
            };
            WhereValue? where = null;
            foreach (var pair in keywords)
            {
                if (pair.Key != "at") throw LookError("fx() has no argument '" + pair.Key + "'", "fx() takes at= (spec-gaps G398)");
                where = pair.Value as WhereValue ?? throw LookError("at= of fx() takes at(...), onto(...) or between(...), not " + pair.Value.KindName, usage);
            }
            if (where == null) throw LookError("fx() needs at= to say where the effect plays", usage);
            return new FxValue(look, where);
        }
    }
}
