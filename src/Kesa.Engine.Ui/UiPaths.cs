using System;

namespace Kesa.Engine.Ui
{
    /// <summary>
    /// <c>&lt;Style src&gt;</c>·<c>Include</c>의 <c>src</c>·USS <c>url()</c>은 모드 폴더 공유 가상 경로만 받는다 (G365).
    /// UI Builder가 쓰는 Unity 프로젝트 경로는 바꾸어 찾지 않고 무엇이 틀렸는지 알린다.
    /// </summary>
    internal static class UiPaths
    {
        /// <summary>모드 경로가 아니면 그 이유, 맞으면 null.</summary>
        public static string? WhyNotModPath(string path)
        {
            bool project = path.IndexOf("://", StringComparison.Ordinal) >= 0
                || path.StartsWith("/", StringComparison.Ordinal) || path.StartsWith("\\", StringComparison.Ordinal)
                || (path.Length >= 2 && path[1] == ':')
                || path.IndexOfAny(new[] { '?', '#' }) >= 0;
            if (project)
                return "'" + path + "' is a Unity project path (the form UI Builder writes); write the path inside the mods instead, like '" + Example(path) + "'";
            foreach (var piece in path.Split('/', '\\'))
                if (piece == "..")
                    return "'" + path + "' climbs out with '..'; paths start at the mods' root, not at this file, like '" + Example(path) + "'";
            return null;
        }

        private static string Example(string path)
        {
            int cut = path.IndexOfAny(new[] { '?', '#' });
            if (cut >= 0) path = path.Substring(0, cut);
            int slash = path.LastIndexOfAny(new[] { '/', '\\' });
            string name = slash >= 0 ? path.Substring(slash + 1) : path;
            return "ui/" + (name.Length > 0 ? name : "screen.uss");
        }
    }
}
