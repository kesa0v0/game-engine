using System.Collections.Generic;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Script;

namespace Kesa.Engine.Runtime
{
    /// <summary>
    /// 거른 읽기에서 보이는 칸의 Ref가 그 연결이 못 보는 엔티티를 가리킬 때의 값 (spec-gaps G198): "누군가 있지만 누군지 모른다".
    /// id가 없어 같은 대상인지도 알 수 없다. 커맨드 인자로는 낼 수 없다.
    /// </summary>
    public sealed class HiddenValue : Value
    {
        public static readonly HiddenValue Instance = new HiddenValue();

        private HiddenValue()
        {
        }

        public override string KindName => "hidden";
    }

    public sealed partial class World
    {
        private readonly PolicyDecl? _viewPolicy;

        /// <summary>
        /// 이 연결 꼬리표로서 보는 거른 읽기 (spec-gaps G196~G203). 상태 읽기·목록·열린 질문이 보기 정책 <c>may_see</c>를 따른다.
        /// 이 월드의 읽기는 그대로 전부 보는 읽기다(서버 자신·테스트·저장). 거른 읽기는 아무것도 바꾸지 않는다.
        /// </summary>
        public WorldView ViewAs(string issuer) => new WorldView(this, issuer);

        /// <summary>
        /// 보기 정책 <c>may_see(issuer, entity, component)</c>. 없으면 모두 보인다. 실패하거나 bool이 아니면 "안 보임"이고
        /// 진단을 남긴다 (G203, 닫는 쪽).
        /// </summary>
        internal bool MaySee(string issuer, EntityId id, string? component, List<Diagnostic> diagnostics)
        {
            if (_viewPolicy == null) return true;
            var function = _viewPolicy.Function;
            string label = "the view policy '" + function.Name + "' for " + id + (component == null ? "" : " and '" + component + "'");
            try
            {
                var value = RunGateFunction(function, new[]
                {
                    new StrValue(issuer), ToScript(new EntityValue(id)),
                    component == null ? (Value)NoneValue.Instance : new StrValue(component),
                }, label);
                if (value is BoolValue b) return b.Value;
                diagnostics.Add(new Diagnostic(Codes.RunViewPolicy, Severity.Error,
                    "computing " + label + ": it must return True or False, not " + value.KindName + "; treated as not seen",
                    _viewPolicy.Mod, function.Definition!.Span, "spec-gaps G203"));
            }
            catch (WorldException e)
            {
                diagnostics.Add(new Diagnostic(Codes.RunViewPolicy, Severity.Error, e.Message + "; treated as not seen",
                    _viewPolicy.Mod, function.Definition!.Span, e.Hint));
            }
            return false;
        }

        /// <summary>질문을 이 연결이 답할 수 있는가: 입구 정책 <c>may_act</c> (G200). 실패하면 답할 수 없음이고 진단을 남긴다.</summary>
        internal bool MayAnswer(string issuer, EntityId who, List<Diagnostic> diagnostics)
        {
            try
            {
                return MayAct(issuer, new EntityValue(who), "a question");
            }
            catch (WorldException e)
            {
                diagnostics.Add(new Diagnostic(e.Code, Severity.Error, e.Message + "; the question is not shown", null,
                    SourceSpan.None, e.Hint));
                return false;
            }
        }

        // 못 보는 엔티티·컴포넌트·줄은 죽은 id·붙지 않은 컴포넌트·없는 줄과 똑같이 응답한다 (G198)

        internal WorldException GoneForView(EntityId id) =>
            new WorldException(Codes.RunDeadEntity, id.Value < 1 || id.Value >= _nextId ? id + " was never created" : id + " was destroyed");

        internal WorldException NotAttachedForView(EntityId id, string component) =>
            new WorldException(Codes.RunNoComponent, id + " (" + DefOf(id) + ") has no '" + component + "'",
                "check with Has, or add it with Add");

        /// <summary>엔티티 키가 죽었을 때 줄 읽기가 던지는 것과 같다 (<c>CheckKey</c>).</summary>
        internal WorldException KeyGoneForView(EntityId key) =>
            new WorldException(Codes.RunDeadEntity, "the key " + RowKey.Entity(key) + " does not exist");

        internal bool IsComponentName(string name) => _schema.TryGetComponent(name, out _);
    }

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
        /// 이 연결이 답할 수 있는 질문만 (질문 대상 중 <c>may_act</c>가 참인 엔티티가 있음, G200). 대상 목록에서 못 보는 엔티티는
        /// 빠지고, 제목·선택지·<c>data</c> 안의 못 보는 엔티티는 <see cref="HiddenValue"/>다(선택지는 번호가 그대로라 뒷면을 보고 고를 수 있다).
        /// </summary>
        public IReadOnlyList<Request> OpenRequests
        {
            get
            {
                var result = new List<Request>();
                foreach (var q in _world.OpenRequests)
                {
                    bool mine = false;
                    foreach (var who in q.Who)
                    {
                        if (_world.IsAlive(who) && _world.MayAnswer(Issuer, who, _diagnostics))
                        {
                            mine = true;
                            break;
                        }
                    }
                    if (mine) result.Add(Filtered(q));
                }
                return result;
            }
        }

        private Request Filtered(Request q)
        {
            var who = new List<EntityId>();
            foreach (var e in q.Who)
                if (Sees(e)) who.Add(e);
            var choices = new List<Value>(q.Choices.Count);
            foreach (var c in q.Choices) choices.Add(Mask(c));
            return new Request(q.Id, q.Flow, q.Command, who, Mask(q.Title), choices, q.Responses, q.ResponseNames, q.Hint, Mask(q.Data))
            {
                BlocksTime = q.BlocksTime,
                Deadline = q.Deadline,
                TimeoutChoice = q.TimeoutChoice,
                TimeoutResponse = q.TimeoutResponse,
            };
        }

        // ---- 값 거르기 ----

        /// <summary>상태의 값: Ref는 <see cref="HiddenValue"/>, 리스트·딕트 안의 못 보는 엔티티는 빠진다 (G198).</summary>
        private Value Filter(Value value)
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
            foreach (var pair in text.Args) args[pair.Key] = Mask(pair.Value);
            return new TextValue(text.Key, args);
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
