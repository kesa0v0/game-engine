using System.Collections.Generic;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Script;

namespace Kesa.Engine.Runtime
{
    public sealed partial class World
    {
        /// <summary>대상(컴포넌트나 Def 타입)마다의 보기 규칙, 나중에 로드된 것이 앞 (G315).</summary>
        private readonly Dictionary<string, List<ViewRuleDecl>> _viewRules = new Dictionary<string, List<ViewRuleDecl>>(System.StringComparer.Ordinal);

        private void SetViewRules(IReadOnlyList<ViewRuleDecl> rules)
        {
            foreach (var rule in rules)
            {
                if (!_viewRules.TryGetValue(rule.Target, out var chain)) _viewRules.Add(rule.Target, chain = new List<ViewRuleDecl>());
                chain.Insert(0, rule);
            }
        }

        /// <summary>
        /// 이 연결 꼬리표로서 보는 거른 읽기 (spec-gaps G196~G203). 상태 읽기·목록·열린 질문이 보기 규칙(<c>@view_rule</c>, G315)을 따른다.
        /// 이 월드의 읽기는 그대로 전부 보는 읽기다(서버 자신·테스트·저장). 거른 읽기는 아무것도 바꾸지 않는다.
        /// </summary>
        public WorldView ViewAs(string issuer) => new WorldView(this, issuer);

        /// <summary>
        /// 이 연결이 앉은 엔티티마다 보기 규칙을 물어 하나라도 보이면 보인다. 앉은 곳이 없으면 <c>viewer=None</c>으로 한 번 묻는다 (G238).
        /// 컴포넌트 층은 그 컴포넌트에, 엔티티 층(<paramref name="component"/>가 null)은 엔티티의 Def 타입에 건 규칙들이 정한다 (G315).
        /// </summary>
        internal bool MaySee(string issuer, EntityId id, string? component, List<Diagnostic> diagnostics)
        {
            string? target = component;
            if (target == null)
            {
                if (!_entities.TryGetValue(id.Value, out var entity)) return true;
                target = entity.Def.Type.Name;
            }
            if (!_viewRules.TryGetValue(target, out var chain)) return true;
            // 규칙은 실제 상태를 읽는다. 보는 쪽으로 부르는 중(G248)이면 잠시 풀고, 규칙이 읽은 곳은 그 영수증에 든다
            var saved = _viewer;
            _viewer = null;
            try
            {
                var seats = SeatsOf(issuer);
                if (seats.Count == 0) return MaySeeAs(chain, NoneValue.Instance, id, component, diagnostics);
                foreach (var viewer in seats)
                    if (MaySeeAs(chain, new EntityValue(viewer), id, component, diagnostics)) return true;
                return false;
            }
            finally
            {
                _viewer = saved;
            }
        }

        /// <summary>
        /// 규칙을 나중에 로드된 것부터 묻고 처음 나온 <c>True</c>/<c>False</c>를 쓴다. <c>None</c>은 앞 규칙에 맡긴다. 모두 <c>None</c>이면 보인다.
        /// 규칙이 실패하거나 셋 밖의 값을 돌려주면 "안 보임"이고 진단을 남긴다 (G203, 닫는 쪽).
        /// </summary>
        private bool MaySeeAs(List<ViewRuleDecl> chain, Value viewer, EntityId id, string? component, List<Diagnostic> diagnostics)
        {
            foreach (var rule in chain)
            {
                var function = rule.Function;
                string label = "the view rule '" + function.Name + "' on '" + rule.Target + "' for " + id +
                               (viewer is EntityValue v ? " seen by " + v.Id : " seen by a spectator");
                try
                {
                    var value = OutsideAct(() => RunGateFunction(function, new[] { ToScript(viewer), ToScript(new EntityValue(id)) }, label));
                    if (value is BoolValue b) return b.Value;
                    if (value is NoneValue) continue;
                    diagnostics.Add(new Diagnostic(Codes.RunViewPolicy, Severity.Error,
                        "computing " + label + ": it must return True, False or None, not " + value.KindName + "; treated as not seen",
                        rule.Mod, function.Definition!.Span, "spec-gaps G203·G315"));
                }
                catch (WorldException e)
                {
                    diagnostics.Add(new Diagnostic(Codes.RunViewPolicy, Severity.Error, e.Message + "; treated as not seen",
                        rule.Mod, function.Definition!.Span, e.Hint));
                }
                return false;
            }
            return true;
        }

        /// <summary>질문을 이 연결이 그 대상으로서 답할 수 있는가: 대상에 앉아 있나 (G200·G237).</summary>
        internal bool MayAnswer(string issuer, EntityId who) => MayAct(issuer, new EntityValue(who));

        /// <summary>
        /// 이 연결이 이 수를 낼 수 있는가: 제출 때처럼 <c>acts_for</c> → 배정 표 (G233·G237). <c>acts_for</c>가 실패하면 낼 수 없음이고
        /// 진단을 남긴다.
        /// </summary>
        internal bool MayIssue(string issuer, string command, IReadOnlyDictionary<string, Value> move, List<Diagnostic> diagnostics)
        {
            try
            {
                return MayAct(issuer, ActsFor(FindCommand(command), Sorted(move)));
            }
            catch (WorldException e)
            {
                diagnostics.Add(new Diagnostic(e.Code, Severity.Error, e.Message + "; the move is not listed", null,
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

}
