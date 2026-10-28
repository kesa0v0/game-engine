using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Kesa.Tools.Decisions
{
    /// <summary>결정 한 건. 머리(---로 싼 `키: 값` 줄)와 본문으로 이루어진 파일 하나.</summary>
    internal sealed class Decision
    {
        public string Id = "";
        public string Area = "";
        public string Title = "";
        public string Status = "";
        public List<string> ChangedBy = new List<string>();
        public List<string> Tags = new List<string>();
        public List<string> Refs = new List<string>();
        public string Summary = "";
        public string Body = "";

        /// <summary>머리에 적힌 키 (빠진 키를 검사가 찾는다).</summary>
        public HashSet<string> Keys = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>읽은 파일 이름(확장자 없이). 파일에서 읽지 않았으면 빈 글.</summary>
        public string FileName = "";

        /// <summary>id가 <c>G숫자</c> 꼴이 아니면 -1.</summary>
        public int Number => Id.Length > 1 && Id[0] == 'G' && int.TryParse(Id.Substring(1), out int n) ? n : -1;

        /// <summary>뒤의 결정이 통째로 갈아 치운 것만 유효하지 않다. 일부 바뀜은 아직 유효하다.</summary>
        public bool Active => Status != "바뀜";

        public static Decision Parse(string text)
        {
            var d = new Decision();
            var lines = text.Replace("\r\n", "\n").Split('\n');
            if (lines.Length == 0 || lines[0] != "---") throw new FormatException("머리가 없다");
            int i;
            for (i = 1; i < lines.Length && lines[i] != "---"; i++)
            {
                int colon = lines[i].IndexOf(':');
                if (colon < 0) continue;
                string key = lines[i].Substring(0, colon).Trim();
                string value = lines[i].Substring(colon + 1).Trim();
                d.Keys.Add(key);
                switch (key)
                {
                    case "id": d.Id = value; break;
                    case "area": d.Area = value; break;
                    case "title": d.Title = value; break;
                    case "status": d.Status = value; break;
                    case "changedBy": d.ChangedBy = Split(value); break;
                    case "tags": d.Tags = Split(value); break;
                    case "refs": d.Refs = Split(value); break;
                    case "summary": d.Summary = value; break;
                }
            }
            d.Body = string.Join("\n", lines.Skip(i + 1)).Trim();
            return d;
        }

        public static List<Decision> LoadAll(string dir) =>
            Directory.GetFiles(dir, "G*.md")
                .Select(f =>
                {
                    var d = Parse(File.ReadAllText(f));
                    d.FileName = Path.GetFileNameWithoutExtension(f);
                    return d;
                })
                .OrderBy(d => d.Number)
                .ToList();

        private static List<string> Split(string value) =>
            value.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
    }
}
