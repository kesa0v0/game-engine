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

        public int Number => int.Parse(Id.Substring(1));

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
                .Select(f => Parse(File.ReadAllText(f)))
                .OrderBy(d => d.Number)
                .ToList();

        private static List<string> Split(string value) =>
            value.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
    }
}
