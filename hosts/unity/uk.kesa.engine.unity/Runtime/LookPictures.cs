#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace Kesa.Engine.Unity
{
    /// <summary>
    /// 판 위 그림 파일 읽기 (G392): 파일은 다른 스레드에서 읽고 텍스처는 <see cref="Pump"/>가 주 스레드에서 만든다. 같은 경로는 한 번 읽어 나눠 쓴다.
    /// </summary>
    internal sealed class LookPictures
    {
        private sealed class Entry
        {
            public Entry(Task<byte[]?> bytes) => Bytes = bytes;

            public readonly Task<byte[]?> Bytes;
            public readonly List<Action<Sprite?>> Waiting = new List<Action<Sprite?>>();
            public bool Done;
            public Sprite? Sprite;
        }

        private readonly IUiAssets _assets;
        private readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private readonly List<(HashSet<string> Paths, TaskCompletionSource<bool> Done)> _preloads = new List<(HashSet<string>, TaskCompletionSource<bool>)>();
        private int _loading;

        public LookPictures(IUiAssets assets)
        {
            _assets = assets;
        }

        public bool Loading => _loading > 0;

        /// <summary>읽혀 있으면 <paramref name="ready"/>를 바로 부르고, 아니면 다 읽은 뒤 <see cref="Pump"/>에서 부른다. 못 읽으면 null.</summary>
        public void Request(string path, Action<Sprite?> ready)
        {
            var entry = Start(path);
            if (entry.Done) ready(entry.Sprite);
            else entry.Waiting.Add(ready);
        }

        public Task Preload(IEnumerable<string> paths)
        {
            var wanted = new HashSet<string>(paths.Where(p => !Start(p).Done), StringComparer.Ordinal);
            if (wanted.Count == 0) return Task.CompletedTask;
            var done = new TaskCompletionSource<bool>();
            _preloads.Add((wanted, done));
            return done.Task;
        }

        /// <summary>다 읽힌 파일로 텍스처를 만들고 기다리던 것을 부른다. 주 스레드에서만.</summary>
        public void Pump()
        {
            if (_loading == 0) return;
            foreach (var pair in _entries.ToList())
            {
                var entry = pair.Value;
                if (entry.Done || !entry.Bytes.IsCompleted) continue;
                entry.Done = true;
                _loading--;
                byte[]? bytes = entry.Bytes.Status == TaskStatus.RanToCompletion ? entry.Bytes.Result : null;
                var texture = new Texture2D(2, 2);
                if (bytes != null && texture.LoadImage(bytes)) entry.Sprite = FromTexture(texture);
                else Debug.LogWarning("Kesa look: the picture '" + pair.Key + "' cannot be read; showing the missing picture");
                foreach (var ready in entry.Waiting) ready(entry.Sprite);
                entry.Waiting.Clear();
                foreach (var preload in _preloads) preload.Paths.Remove(pair.Key);
            }
            for (int i = _preloads.Count - 1; i >= 0; i--)
            {
                if (_preloads[i].Paths.Count > 0) continue;
                _preloads[i].Done.SetResult(true);
                _preloads.RemoveAt(i);
            }
        }

        // 긴 변을 1 단위로: 그림 해상도가 바뀌어도 판 위 크기는 그대로다
        public static Sprite FromTexture(Texture2D texture) =>
            Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), Math.Max(texture.width, texture.height));

        private Entry Start(string path)
        {
            if (_entries.TryGetValue(path, out var entry)) return entry;
            var assets = _assets;
            entry = new Entry(Task.Run(() => assets.ReadBytes(path)));
            _entries[path] = entry;
            _loading++;
            return entry;
        }
    }
}
