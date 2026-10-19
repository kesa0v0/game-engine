using System.Collections.Generic;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Script;

namespace Kesa.Engine.Runtime
{
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

        /// <summary>
        /// 이 연결이 이 수를 낼 수 있는가: 제출 때처럼 <c>acts_for</c> → <c>may_act</c> (G233). 실패하면 낼 수 없음이고 진단을 남긴다.
        /// </summary>
        internal bool MayIssue(string issuer, string command, IReadOnlyDictionary<string, Value> move, List<Diagnostic> diagnostics)
        {
            try
            {
                return MayAct(issuer, ActsFor(FindCommand(command), Sorted(move)), command);
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
