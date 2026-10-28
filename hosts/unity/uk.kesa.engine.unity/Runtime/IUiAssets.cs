#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kesa.Engine.Unity
{
    /// <summary>화면이 쓰는 파일(UXML·USS 글, 이미지)을 가상 경로로 찾는다 (spec-gaps G335·G362).</summary>
    public interface IUiAssets
    {
        /// <summary><c>Include</c>·<c>Style</c>의 <c>src</c>로 UXML·USS 글을 찾는다. 없으면 null. 해석기의 <c>files</c>로 넘긴다.</summary>
        string? LoadText(string path);

        /// <summary>이미지를 찾는다. 없으면 null.</summary>
        Texture2D? LoadImage(string path);
    }

    /// <summary>
    /// 모드 폴더들을 하나의 가상 경로 공간으로 겹쳐 읽는다: 같은 경로가 여럿이면 나중에 로드된 모드의 파일을 쓴다 (G335).
    /// 게임 빌드 안의 스타일시트는 여기서 찾지 않고 게임 코드가 화면 맨 바깥에 붙인다 (G362).
    /// </summary>
    public sealed class ModFolderAssets : IUiAssets
    {
        private readonly IReadOnlyList<string> _folders;
        private readonly Dictionary<string, Texture2D?> _images = new Dictionary<string, Texture2D?>(StringComparer.Ordinal);

        /// <param name="folders">모드 폴더, 로드 순서대로.</param>
        public ModFolderAssets(IReadOnlyList<string> folders)
        {
            _folders = folders;
        }

        /// <summary>
        /// 가상 경로를 실제 파일로. 나중에 로드된 모드부터 찾는다. 없거나 모드 폴더 안의 상대 경로가 아니면(절대 경로, <c>..</c>) null.
        /// </summary>
        public string? Find(string path)
        {
            if (!IsInside(path)) return null;
            for (int i = _folders.Count - 1; i >= 0; i--)
            {
                string full = Path.Combine(_folders[i], path);
                if (File.Exists(full)) return full;
            }
            return null;
        }

        // Path.Combine은 절대 경로면 폴더를 버리고 ..는 폴더 밖으로 나가므로 G335의 "모드 폴더 안의 상대 경로"를 여기서 지킨다
        private static bool IsInside(string path)
        {
            if (path.Length == 0 || Path.IsPathRooted(path) || path.IndexOf(':') >= 0) return false;
            foreach (var segment in path.Split('/', '\\'))
                if (segment == "..") return false;
            return true;
        }

        public string? LoadText(string path)
        {
            string? file = Find(path);
            return file == null ? null : File.ReadAllText(file);
        }

        public Texture2D? LoadImage(string path)
        {
            if (_images.TryGetValue(path, out var cached)) return cached;
            string? file = Find(path);
            Texture2D? texture = null;
            if (file != null)
            {
                var loaded = new Texture2D(2, 2);
                if (loaded.LoadImage(File.ReadAllBytes(file))) texture = loaded;
            }
            _images[path] = texture;
            return texture;
        }
    }
}
