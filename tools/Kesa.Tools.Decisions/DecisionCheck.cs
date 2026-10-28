using System.Collections.Generic;
using System.Linq;

namespace Kesa.Tools.Decisions
{
    /// <summary>결정 파일들의 머리가 CLAUDE.md "결정 기록"의 규칙을 지키는지. 문제마다 한 줄.</summary>
    internal static class DecisionCheck
    {
        public static readonly string[] Statuses = { "확인", "잠정", "바뀜", "일부바뀜" };

        private static readonly string[] RequiredKeys = { "id", "area", "title", "status", "changedBy", "tags", "refs", "summary" };

        public const string ChangedMark = "**바뀜:**";

        public static List<string> Run(IReadOnlyList<Decision> all)
        {
            var problems = new List<string>();
            var byId = new Dictionary<string, Decision>();
            foreach (var d in all)
            {
                string where = d.FileName.Length > 0 ? d.FileName : d.Id;
                if (d.Number < 0) problems.Add(where + ": id '" + d.Id + "' is not G<number>");
                if (d.FileName.Length > 0 && d.FileName != d.Id) problems.Add(where + ": the file name differs from id " + d.Id);
                if (!byId.ContainsKey(d.Id)) byId[d.Id] = d;
                else problems.Add(where + ": id " + d.Id + " is used twice");
                foreach (var key in RequiredKeys)
                    if (!d.Keys.Contains(key)) problems.Add(where + ": the header has no '" + key + "'");
                if (d.Title.Length == 0 || d.Summary.Length == 0) problems.Add(where + ": empty title or summary");
                if (d.Tags.Count == 0) problems.Add(where + ": no tags");
            }

            foreach (var d in all)
            {
                if (!Statuses.Contains(d.Status))
                {
                    problems.Add(d.Id + ": status '" + d.Status + "' is not one of " + string.Join("·", Statuses));
                    continue;
                }
                bool changed = d.Status == "바뀜" || d.Status == "일부바뀜";
                if (changed && d.ChangedBy.Count == 0) problems.Add(d.Id + ": status " + d.Status + " but changedBy is empty");
                if (!changed && d.ChangedBy.Count > 0) problems.Add(d.Id + ": changedBy is set but status is " + d.Status);
                if (changed && !d.Body.Contains(ChangedMark)) problems.Add(d.Id + ": status " + d.Status + " but the body has no " + ChangedMark + " line");
                foreach (var later in d.ChangedBy)
                {
                    if (!byId.TryGetValue(later, out var by)) problems.Add(d.Id + ": changedBy " + later + " does not exist");
                    else if (by.Number <= d.Number) problems.Add(d.Id + ": changedBy " + later + " is not a later decision");
                }
                foreach (var r in d.Refs)
                    if (!byId.ContainsKey(r)) problems.Add(d.Id + ": refs " + r + " does not exist");
            }
            return problems;
        }
    }
}
