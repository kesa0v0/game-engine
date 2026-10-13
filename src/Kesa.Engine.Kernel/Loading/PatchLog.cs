using System;
using System.Collections.Generic;

namespace Kesa.Engine.Loading
{
    /// <summary>패치 항목의 처리 상태. ③은 Pending으로 만들고 ④가 적용하면서 채운다.</summary>
    public enum PatchStatus
    {
        Pending,
    }

    /// <summary>
    /// 변경 이력의 항목 하나 (spec S1·S9, spec-gaps G45): 어떤 모드가 어느 Def의 어느 필드를 어떤 연산으로 어떤 값으로
    /// 바꿨는지와 호출 위치. 같은 Def에서는 적용 순서(모드 로드 순서, 같은 모드 안에서는 호출 순서)대로 쌓인다.
    /// </summary>
    public sealed class PatchLogEntry
    {
        internal PatchLogEntry(int sequence, ResolvedPatch patch)
        {
            Sequence = sequence;
            Patch = patch;
        }

        /// <summary>로그 전체에서의 순번 (0부터).</summary>
        public int Sequence { get; }

        public ResolvedPatch Patch { get; }
        public PatchCall Call => Patch.Call;
        public string Mod => Patch.Call.Mod;
        public string TargetId => Patch.Call.TargetId;
        public PatchOp Op => Patch.Call.Op;
        public PatchPath Path => Patch.Call.Path;
        public PatchStatus Status { get; internal set; } = PatchStatus.Pending;
    }

    /// <summary>
    /// ③이 만든 패치 변경 이력 (G45). <c>DefRecord</c>는 건드리지 않는 별도 객체이고 ④·⑥과 덤프(S9)는 읽기만 한다.
    /// 모드가 비활성화되면 G49대로 처음부터 다시 만들므로 이 객체는 회차마다 새로 생긴다.
    /// </summary>
    public sealed class PatchLog
    {
        private readonly List<PatchLogEntry> _entries = new List<PatchLogEntry>();

        internal PatchLog(IReadOnlyList<ResolvedPatch> patches)
        {
            foreach (var p in patches) _entries.Add(new PatchLogEntry(_entries.Count, p));
        }

        public IReadOnlyList<PatchLogEntry> Entries => _entries;

        /// <summary>그 Def의 항목들을 적용 순서대로.</summary>
        public IEnumerable<PatchLogEntry> ForDef(string defId)
        {
            foreach (var e in _entries)
                if (string.Equals(e.TargetId, defId, StringComparison.Ordinal)) yield return e;
        }

        /// <summary>
        /// 그 Def의 그 필드를 건드리는 항목들. 경로가 겹치는 것(같거나, 한쪽이 다른 쪽의 앞부분)을 모두 돌려준다 (G50).
        /// </summary>
        public IEnumerable<PatchLogEntry> ForField(string defId, PatchPath path)
        {
            foreach (var e in ForDef(defId))
                if (e.Path.IsPrefixOf(path) || path.IsPrefixOf(e.Path)) yield return e;
        }
    }
}
