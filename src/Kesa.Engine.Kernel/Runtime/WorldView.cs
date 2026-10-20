using System.Collections.Generic;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Script;

namespace Kesa.Engine.Runtime
{
    /// <summary>
    /// 한 연결 꼬리표로서 본 월드 (spec-gaps G196~G200). 못 보는 엔티티는 없는 것(죽은 id와 같은 응답)이고, 못 보는 컴포넌트는
    /// 붙지 않은 것이며, 보이는 칸의 Ref가 못 보는 엔티티를 가리키면 <see cref="HiddenValue"/>다. Ref 목록과 역방향 컬렉션에서는
    /// 못 보는 엔티티가 빠진다. 엔티티가 보이면 Def와 Def 타입 상수도 보인다(G197). 보이는 <c>derived</c>는 숨긴 칸으로
    /// 계산돼도 결과가 보인다(G199). 정책은 읽을 때마다 부른다.
    /// </summary>
    public sealed class WorldView
    {
        private readonly World _world;
        private readonly List<Diagnostic> _diagnostics = new List<Diagnostic>();

        internal WorldView(World world, string issuer)
        {
            _world = world;
            Issuer = issuer;
        }

        /// <summary>보는 연결의 꼬리표 (G197).</summary>
        public string Issuer { get; }

        /// <summary>정책이 실패해 "안 보임"이나 "답할 수 없음"으로 처리한 기록 (G203). 서버 로그용이고 플레이어에게 보내지 않는다.</summary>
        public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics;

        /// <summary>이 엔티티가 살아 있고 이 연결에게 보이는가.</summary>
        public bool Sees(EntityId id) => _world.IsAlive(id) && _world.MaySee(Issuer, id, null, _diagnostics);

        /// <summary>이 엔티티와 그 컴포넌트가 보이는가 (붙어 있는지는 보지 않는다).</summary>
        public bool Sees(EntityId id, string component) => Sees(id) && _world.MaySee(Issuer, id, component, _diagnostics);

        public bool IsAlive(EntityId id) => Sees(id);

        public string DefOf(EntityId id)
        {
            Visible(id);
            return _world.DefOf(id);
        }

        public bool Has(EntityId id, string component)
        {
            Visible(id);
            return _world.Has(id, component) && _world.MaySee(Issuer, id, component, _diagnostics);
        }

        public Value Get(EntityId id, MemberHandle member)
        {
            Visible(id);
            if (member.Component != null && !_world.MaySee(Issuer, id, member.Component.Name, _diagnostics))
                throw _world.NotAttachedForView(id, member.Component.Name);
            return Filter(_world.Get(id, member));
        }

        // ---- 모드 함수 부르기 (G248) ----

        /// <summary>
        /// <see cref="World.Call"/>을 이 연결로서: 함수 안의 모든 읽기가 이 거른 읽기를 거친다(못 보는 엔티티는 없는 것, 못 보는 컴포넌트는
        /// 붙지 않은 것, Ref는 <see cref="HiddenValue"/>). 결과는 따로 거르지 않는다. 영수증에는 보기 정책이 읽은 곳과 배정 표도 든다.
        /// </summary>
        public CallResult Call(string function, params Value[] arguments) => _world.CallAs(this, function, arguments);

        /// <summary><see cref="World.CheckCall"/>과 같다: 부를 수 있는지는 보는 쪽과 상관없다.</summary>
        public string? CheckCall(string function, int argumentCount) => _world.CheckCall(function, argumentCount);

        // ---- 키 컴포넌트의 줄 ----

        // 못 보는 컴포넌트의 줄은 붙지 않은 컴포넌트의 줄처럼, 못 보는 엔티티 키는 죽은 키처럼 응답한다 (G198)

        public Value GetRow(EntityId id, MemberHandle member, string key)
        {
            RowVisible(id, member);
            return Filter(_world.GetRow(id, member, key));
        }

        public Value GetRow(EntityId id, MemberHandle member, EntityId key)
        {
            RowVisible(id, member);
            if (_world.IsAlive(key) && !Sees(key)) throw _world.KeyGoneForView(key);
            return Filter(_world.GetRow(id, member, key));
        }

        public Value GetRow(EntityId id, MemberHandle member, long key)
        {
            RowVisible(id, member);
            return Filter(_world.GetRow(id, member, key));
        }

        public bool HasRow(EntityId id, string component, string key) =>
            Has(id, component) && _world.HasRow(id, component, key);

        public bool HasRow(EntityId id, string component, EntityId key)
        {
            if (!Has(id, component)) return false;
            if (_world.IsAlive(key) && !Sees(key)) throw _world.KeyGoneForView(key);
            return _world.HasRow(id, component, key);
        }

        public bool HasRow(EntityId id, string component, long key) =>
            Has(id, component) && _world.HasRow(id, component, key);

        public IReadOnlyList<string> RowKeys(EntityId id, string component)
        {
            VisibleComponent(id, component);
            return _world.RowKeys(id, component);
        }

        public IReadOnlyList<long> RowInts(EntityId id, string component)
        {
            VisibleComponent(id, component);
            return _world.RowInts(id, component);
        }

        /// <summary>엔티티 키 줄의 키들. 못 보는 엔티티가 키인 줄은 빠진다 (G198).</summary>
        public IReadOnlyList<EntityId> RowEntities(EntityId id, string component)
        {
            VisibleComponent(id, component);
            var keys = new List<EntityId>();
            foreach (var key in _world.RowEntities(id, component))
                if (Sees(key)) keys.Add(key);
            return keys;
        }

        // ---- 목록 ----

        public IReadOnlyList<EntityId> All(string typeName)
        {
            var result = new List<EntityId>();
            foreach (var id in _world.All(typeName))
                if (Sees(id)) result.Add(id);
            return result;
        }

        /// <summary>
        /// <see cref="World.Query"/>를 보이는 것만으로: 엔티티가 보이고 이름으로 고른 컴포넌트가 모두 보여야 한다(못 보는 컴포넌트는
        /// 붙지 않은 것, G198).
        /// </summary>
        public IReadOnlyList<EntityId> Query(params string[] names)
        {
            var result = new List<EntityId>();
            foreach (var id in _world.Query(names))
            {
                bool seen = Sees(id);
                foreach (var name in names)
                {
                    if (!seen) break;
                    if (_world.IsComponentName(name)) seen = _world.MaySee(Issuer, id, name, _diagnostics);
                }
                if (seen) result.Add(id);
            }
            return result;
        }

        /// <summary>역방향 컬렉션. 가리키는 쪽이 안 보이거나 Ref가 든 컴포넌트가 안 보이면 빠진다 (G198).</summary>
        public IReadOnlyList<EntityId> Inverse(EntityId target, MemberHandle reference)
        {
            Visible(target);
            var result = new List<EntityId>();
            foreach (var source in _world.Inverse(target, reference))
                if (Sees(source) && _world.MaySee(Issuer, source, reference.Component!.Name, _diagnostics)) result.Add(source);
            return result;
        }

        // ---- 열린 질문 ----

        /// <summary>
        /// 이 연결이 답할 수 있는 질문만 (질문 대상 중 이 연결이 앉은 엔티티가 있음, G200·G237). 대상 목록에는 이 연결이 답할 수 있고
        /// 보이는 대상만 남고(G234), 제목·선택지·<c>data</c> 안의 못 보는 엔티티는 <see cref="HiddenValue"/>다(선택지는 번호가 그대로라
        /// 뒷면을 보고 고를 수 있다).
        /// </summary>
        public IReadOnlyList<Request> OpenRequests
        {
            get
            {
                var result = new List<Request>();
                foreach (var q in _world.OpenRequests)
                {
                    var mine = ForMe(q);
                    if (mine != null) result.Add(mine);
                }
                return result;
            }
        }

        /// <summary>이 연결이 답할 수 있으면 거른 질문, 아니면 null (G200). 대상은 답할 수 있는 것만 (G234).</summary>
        private Request? ForMe(Request? q)
        {
            if (q == null) return null;
            var answerable = new List<EntityId>();
            foreach (var who in q.Who)
                if (_world.IsAlive(who) && _world.MayAnswer(Issuer, who)) answerable.Add(who);
            return answerable.Count == 0 ? null : Filtered(q, answerable);
        }

        private Request Filtered(Request q, List<EntityId> answerable)
        {
            var who = new List<EntityId>();
            foreach (var e in answerable)
                if (Sees(e)) who.Add(e);
            var choices = new List<OptionValue>(q.Choices.Count);
            foreach (var c in q.Choices)
            {
                choices.Add(new OptionValue(Mask(c.Value), c.Label == null ? null : Mask(c.Label), c.Desc == null ? null : Mask(c.Desc),
                    c.Style, c.Data == null ? null : Mask(c.Data)));
            }
            return new Request(q.Id, q.Flow, q.Command, who, Mask(q.Title), choices, q.Responses, q.ResponseNames, q.Hint, Mask(q.Data))
            {
                BlocksTime = q.BlocksTime,
                Deadline = q.Deadline,
                TimeoutChoice = q.TimeoutChoice,
                TimeoutResponse = q.TimeoutResponse,
            };
        }

        // ---- 제출·답의 결과 ----

        /// <summary>
        /// 제출 결과를 이 연결에게 보낼 모양으로 (G201). 거부 사유의 문구 인자·대상·인자 안의 못 보는 엔티티는 <see cref="HiddenValue"/>이고
        /// 그 밖의 값은 모드가 쓴 대로다. 반응 목록은 빠지고, 스크립트 오류는 코드만, 인자 거부는 문제 종류와 매개변수 이름만 남는다.
        /// 연 질문은 이 연결이 답할 수 있을 때만 있다(G200).
        /// </summary>
        public SubmitResult Show(SubmitResult result)
        {
            return new SubmitResult(result.Command, result.Issuer, result.Outcome)
            {
                Arguments = MaskArguments(result.Arguments),
                ArgumentRejections = Bare(result.ArgumentRejections),
                Target = result.Target == null ? null : Mask(result.Target),
                Preconditions = Show(result.Preconditions),
                Error = Bare(result.Error),
                OpenedRequest = ForMe(result.OpenedRequest),
                SleepsUntil = result.SleepsUntil,
            };
        }

        /// <summary>답·응답 결과를 이 연결에게 보낼 모양으로 (G201). 거절 이유 본문(<see cref="AnswerResult.Message"/>)도 빠진다.</summary>
        public AnswerResult Show(AnswerResult result)
        {
            return new AnswerResult(result.Request, result.Issuer, result.Outcome)
            {
                Arguments = MaskArguments(result.Arguments),
                ArgumentRejections = Bare(result.ArgumentRejections),
                Preconditions = Show(result.Preconditions),
                Error = Bare(result.Error),
                OpenedRequest = ForMe(result.OpenedRequest),
                SleepsUntil = result.SleepsUntil,
            };
        }

        // ---- 근거 (G199·G205) ----

        /// <summary>
        /// 보이는 <c>derived</c> 값과 그 근거. 값은 보인다(그 칸을 공개한 것은 모드의 결정, G199). 근거 중 못 보는 엔티티·컴포넌트를
        /// 읽은 것과 못 보는 출처의 기여 행은 빠지고, 그런 것이 있었던 단계마다 끝에 <see cref="ExplainKind.Hidden"/> 하나가 붙는다(G205).
        /// <c>query</c> 근거의 목록에서는 못 보는 엔티티가 빠진다. 정책은 월드가 근거를 다 모은 뒤에 부른다.
        /// </summary>
        public ExplainNode Explain(EntityId id, MemberHandle member)
        {
            Visible(id);
            if (member.Component != null && !_world.MaySee(Issuer, id, member.Component.Name, _diagnostics))
                throw _world.NotAttachedForView(id, member.Component.Name);
            return Strip(_world.Explain(id, member));
        }

        public ExplainNode Explain(EntityId id, MemberHandle member, string key)
        {
            RowVisible(id, member);
            return Strip(_world.Explain(id, member, key));
        }

        public ExplainNode Explain(EntityId id, MemberHandle member, EntityId key)
        {
            RowVisible(id, member);
            if (_world.IsAlive(key) && !Sees(key)) throw _world.KeyGoneForView(key);
            return Strip(_world.Explain(id, member, key));
        }

        private ExplainNode Strip(ExplainNode node) => node.With(Filter(node.Value), Strip(node.Reads));

        private IReadOnlyList<ExplainNode> Strip(IReadOnlyList<ExplainNode> reads)
        {
            var kept = new List<ExplainNode>(reads.Count);
            bool hidden = false;
            foreach (var read in reads)
            {
                switch (read.Kind)
                {
                    case ExplainKind.Time:
                        kept.Add(read);
                        continue;
                    case ExplainKind.Query:
                    {
                        var items = new List<Value>();
                        foreach (var item in ((ListValue)read.Value).Items)
                            if (SeesListed(((IEntityReference)item).EntityId, read.Names)) items.Add(item);
                        kept.Add(read.With(new ListValue(items), read.Reads));
                        continue;
                    }
                    case ExplainKind.Contribution:
                        if (SeesRead(read) && (read.Source == null || !_world.IsAlive(read.Source.Value) || Sees(read.Source.Value)))
                            kept.Add(read.With(Filter(read.Value), read.Reads));
                        else hidden = true;
                        continue;
                }
                if (SeesRead(read)) kept.Add(Strip(read));
                else hidden = true;
            }
            if (hidden) kept.Add(ExplainNode.Hidden());
            return kept;
        }

        /// <summary>읽은 칸이 보이는가: 엔티티, 컴포넌트(Def 상수면 엔티티 층, G197), 엔티티 키.</summary>
        private bool SeesRead(ExplainNode read)
        {
            if (!_world.IsAlive(read.Entity)) return true;
            if (_world.IsComponentName(read.Component) ? !Sees(read.Entity, read.Component) : !Sees(read.Entity)) return false;
            return read.Key == null || !read.Key.StartsWith("#", System.StringComparison.Ordinal) ||
                   !long.TryParse(read.Key.Substring(1), System.Globalization.NumberStyles.None,
                       System.Globalization.CultureInfo.InvariantCulture, out var key) ||
                   !IsUnseenEntity(new EntityValue(new EntityId(key)));
        }

        /// <summary><c>query</c>가 꺼낸 엔티티를 이 연결도 같은 query로 꺼내는가 (<see cref="Query"/>와 같다).</summary>
        private bool SeesListed(EntityId id, IReadOnlyList<string> names)
        {
            if (!Sees(id)) return false;
            foreach (var name in names)
                if (_world.IsComponentName(name) && !_world.MaySee(Issuer, id, name, _diagnostics)) return false;
            return true;
        }

        // ---- 합법 수와 후보 (G202) ----

        /// <summary>
        /// 월드의 <see cref="World.Candidates"/>에서 이 연결이 못 보는 엔티티가 든 후보(리스트·딕트 안까지)를 뺀 것. 실제 상태로 평가하므로
        /// 합법 여부로 드러나는 것은 모드의 설계다. 인자 거절은 <see cref="Show(SubmitResult)"/>처럼 본문이 빠진다.
        /// </summary>
        public CandidateList Candidates(string command, IReadOnlyDictionary<string, Value> args, string parameter)
        {
            var all = _world.Candidates(command, args, parameter);
            var values = new List<Value>(all.Values.Count);
            foreach (var v in all.Values)
                if (AllSeen(v)) values.Add(v);
            return new CandidateList(all.Command, all.Parameter, values, all.Needs, Bare(all.Arguments));
        }

        /// <summary>
        /// 월드의 <see cref="World.LegalMoves"/>에서 이 연결이 못 보는 엔티티가 든 수(G202)와 이 연결이 낼 수 없는 수(입구 정책, G233)를
        /// 뺀 것. <paramref name="max"/>와 <see cref="LegalMoveList.Truncated"/>는 남은 수만 센다(아니면 "상한보다 적은데 잘림"으로
        /// 숨긴 수가 샌다).
        /// </summary>
        public LegalMoveList LegalMoves(string command, IReadOnlyDictionary<string, Value> args, int max)
        {
            var moves = _world.LegalMoves(command, args, max, move =>
            {
                foreach (var v in move.Values)
                    if (!AllSeen(v)) return false;
                return _world.MayIssue(Issuer, command, move, _diagnostics);
            });
            return new LegalMoveList(moves.Command, moves.Moves, moves.Truncated, Bare(moves.Arguments));
        }

        /// <summary>
        /// 월드의 <see cref="World.LegalResponses"/>를 이 연결이 답할 수 있는 대상(G200)으로 좁히고 못 보는 엔티티가 든 수를 뺀 것 (G232).
        /// 이 연결이 답할 수 없는 질문은 <see cref="OpenRequests"/>에 없듯 없는 질문과 같은 오류다.
        /// </summary>
        public ResponseMoveList LegalResponses(long request, EntityId? by, int max)
        {
            Request? open = null;
            foreach (var q in _world.OpenRequests)
                if (q.Id == request) open = q;
            if (ForMe(open) == null) throw World.NoRequest(request);
            return _world.LegalResponses(request, by, max,
                who => _world.IsAlive(who) && _world.MayAnswer(Issuer, who),
                move =>
                {
                    foreach (var v in move.Values)
                        if (!AllSeen(v)) return false;
                    return true;
                });
        }

        /// <summary>값 안의 살아 있는 엔티티가 모두 보이는가.</summary>
        private bool AllSeen(Value value)
        {
            switch (value)
            {
                case EntityValue _:
                    return !IsUnseenEntity(value);
                case ListValue list:
                    foreach (var item in list.Items)
                        if (!AllSeen(item)) return false;
                    return true;
                case DictValue dict:
                    foreach (var item in dict.Items)
                        if (!AllSeen(item.Key) || !AllSeen(item.Value)) return false;
                    return true;
            }
            return true;
        }

        /// <summary>
        /// 시간 진행 결과를 이 연결에게 보낼 모양으로 (G206). 진행한 틱과 멈췄는지는 그대로이고, 멈춘 이유의 질문은 이 연결이 답할 수
        /// 있는 것만 거른 모양으로 남는다(G200). 반응 목록은 빠진다(G201).
        /// </summary>
        public AdvanceResult Show(AdvanceResult result)
        {
            var mine = new List<Request>();
            foreach (var q in result.BlockedBy)
            {
                var shown = ForMe(q);
                if (shown != null) mine.Add(shown);
            }
            var view = new AdvanceResult(result.Advanced, result.Tick, System.Array.Empty<Reaction>()) { BlockedBy = mine };
            view.Blocked = result.Blocked;
            return view;
        }

        private PreconditionReport? Show(PreconditionReport? report)
        {
            if (report == null) return null;
            var requires = new List<RequireOutcome>(report.Requires.Count);
            foreach (var r in report.Requires) requires.Add(Show(r));
            return new PreconditionReport(report.Command, Bare(report.Arguments), requires);
        }

        private RequireOutcome Show(RequireOutcome r)
        {
            switch (r.Kind)
            {
                case PreconditionKind.Choose:
                    return r;
                case PreconditionKind.Cost:
                    // 칸(#3.Treasury.gold)의 엔티티나 컴포넌트를 못 보면 칸과 가진 양은 없는 것이다 (G198). 필요한 양은 모드가 쓴 식이다
                    if (r.Slot != null && !SeesSlot(r.Slot))
                        return RequireOutcome.Cost(r.Status, null, r.Have == null ? null : HiddenValue.Instance, r.Need, r.Span);
                    return r;
            }
            var args = new SortedDictionary<string, Value>(System.StringComparer.Ordinal);
            foreach (var pair in r.Args) args[pair.Key] = Mask(pair.Value);
            return new RequireOutcome(r.Status, r.MessageKey, args, r.Span);
        }

        /// <summary><c>SlotName</c>의 모양 <c>#id.Component[key].field</c>에서 엔티티와 컴포넌트를 읽어 본다.</summary>
        private bool SeesSlot(string slot)
        {
            int dot = slot.IndexOf('.');
            if (dot < 2 || slot[0] != '#' || !long.TryParse(slot.Substring(1, dot - 1), System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out var id))
                return false;
            int end = slot.IndexOfAny(new[] { '.', '[' }, dot + 1);
            var component = end < 0 ? slot.Substring(dot + 1) : slot.Substring(dot + 1, end - dot - 1);
            var entity = new EntityId(id);
            return !_world.IsAlive(entity) || Sees(entity, component);
        }

        private IReadOnlyDictionary<string, Value>? MaskArguments(IReadOnlyDictionary<string, Value>? args)
        {
            if (args == null) return null;
            var masked = new SortedDictionary<string, Value>(System.StringComparer.Ordinal);
            foreach (var pair in args) masked[pair.Key] = Mask(pair.Value);
            return masked;
        }

        private static IReadOnlyList<ArgumentRejection> Bare(IReadOnlyList<ArgumentRejection> rejections)
        {
            if (rejections.Count == 0) return rejections;
            var bare = new List<ArgumentRejection>(rejections.Count);
            foreach (var r in rejections) bare.Add(new ArgumentRejection(r.Parameter, r.Parameter, r.Problem, r.Expected, ""));
            return bare;
        }

        private static WorldException? Bare(WorldException? error) =>
            error == null ? null : new WorldException(error.Code, "script error");

        // ---- 값 거르기 ----

        /// <summary>상태의 값: Ref는 <see cref="HiddenValue"/>, 리스트·딕트 안의 못 보는 엔티티는 빠진다 (G198).</summary>
        internal Value Filter(Value value)
        {
            switch (value)
            {
                case EntityValue e:
                    return !_world.IsAlive(e.Id) || Sees(e.Id) ? value : HiddenValue.Instance;
                case ListValue list:
                {
                    var items = new List<Value>(list.Items.Count);
                    foreach (var item in list.Items)
                        if (!IsUnseenEntity(item)) items.Add(Filter(item));
                    return new ListValue(items);
                }
                case DictValue dict:
                {
                    var items = new List<DictItem>(dict.Items.Count);
                    foreach (var item in dict.Items)
                        if (!IsUnseenEntity(item.Key)) items.Add(new DictItem(item.Key, Filter(item.Value), item.Span));
                    return new DictValue(items);
                }
                case TextValue text:
                    return MaskText(text);
            }
            return value;
        }

        /// <summary>질문·문구 안의 값: 못 보는 엔티티는 자리를 지킨 채 <see cref="HiddenValue"/>다 (G200·G201).</summary>
        private Value Mask(Value value)
        {
            switch (value)
            {
                case EntityValue e:
                    return !_world.IsAlive(e.Id) || Sees(e.Id) ? value : HiddenValue.Instance;
                case ListValue list:
                {
                    var items = new List<Value>(list.Items.Count);
                    foreach (var item in list.Items) items.Add(Mask(item));
                    return new ListValue(items);
                }
                case DictValue dict:
                {
                    var items = new List<DictItem>(dict.Items.Count);
                    foreach (var item in dict.Items) items.Add(new DictItem(Mask(item.Key), Mask(item.Value), item.Span));
                    return new DictValue(items);
                }
                case TextValue text:
                    return MaskText(text);
            }
            return value;
        }

        private TextValue MaskText(TextValue text)
        {
            var args = new SortedDictionary<string, Value>(System.StringComparer.Ordinal);
            var defs = new SortedDictionary<string, string>(System.StringComparer.Ordinal);
            foreach (var pair in text.Args)
            {
                args[pair.Key] = Mask(pair.Value);
                // 가린 엔티티의 Def는 그 엔티티가 무엇인지 알려 주므로 함께 뺀다 (G267)
                if (!(args[pair.Key] is HiddenValue) && text.EntityDefs.TryGetValue(pair.Key, out var def)) defs[pair.Key] = def;
            }
            return new TextValue(text.Key, args, defs);
        }

        private bool IsUnseenEntity(Value value) => value is EntityValue e && _world.IsAlive(e.Id) && !Sees(e.Id);

        private void Visible(EntityId id)
        {
            if (!Sees(id)) throw _world.GoneForView(id);
        }

        private void VisibleComponent(EntityId id, string component)
        {
            Visible(id);
            if (!_world.MaySee(Issuer, id, component, _diagnostics)) throw _world.NotAttachedForView(id, component);
        }

        /// <summary>그 줄의 엔티티와 컴포넌트가 보여야 한다 (아니면 죽은 id·붙지 않은 컴포넌트로 던진다).</summary>
        private void RowVisible(EntityId id, MemberHandle member)
        {
            Visible(id);
            if (!_world.MaySee(Issuer, id, member.Component!.Name, _diagnostics))
                throw _world.NotAttachedForView(id, member.Component.Name);
        }
    }
}
