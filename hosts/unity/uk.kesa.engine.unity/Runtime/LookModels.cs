#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace Kesa.Engine.Unity
{
    /// <summary>판 위 모델 파일 (G391): 확장자로 로더를 고르고, 같은 경로는 한 번 읽어 그 원본을 복제해 쓴다. 원본은 꺼진 채 <c>holder</c> 아래에 있다.</summary>
    internal sealed class LookModels
    {
        private sealed class Entry
        {
            public readonly List<Action<GameObject?>> Waiting = new List<Action<GameObject?>>();
            public bool Done;
            public GameObject? Template;
        }

        private readonly IUiAssets _assets;
        private readonly Transform _holder;
        private readonly Dictionary<string, IModelLoader> _loaders = new Dictionary<string, IModelLoader>(StringComparer.Ordinal);
        private readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private int _loading;

        public LookModels(IUiAssets assets, Transform holder)
        {
            _assets = assets;
            _holder = holder;
        }

        public bool Loading => _loading > 0;

        public void Add(IModelLoader loader)
        {
            foreach (var extension in loader.Extensions) _loaders[extension.ToLowerInvariant()] = loader;
        }

        /// <summary>읽혀 있으면 <paramref name="ready"/>를 바로 부르고, 아니면 다 읽은 뒤 부른다. 못 읽으면 null.</summary>
        public void Request(string path, Action<GameObject?> ready)
        {
            if (!_entries.TryGetValue(path, out var entry))
            {
                entry = new Entry();
                _entries[path] = entry;
                _ = Start(path, entry);
            }
            if (entry.Done) ready(entry.Template);
            else entry.Waiting.Add(ready);
        }

        private async Task Start(string path, Entry entry)
        {
            _loading++;
            try
            {
                if (_loaders.TryGetValue(Path.GetExtension(path).ToLowerInvariant(), out var loader))
                    entry.Template = await loader.Load(path, _assets, _holder);
                else Debug.LogWarning("Kesa look: no model loader takes '" + path + "' (spec-gaps G391)");
            }
            catch (Exception e)
            {
                Debug.LogWarning("Kesa look: the model '" + path + "' failed to load: " + e.Message);
            }
            if (entry.Template == null) Debug.LogWarning("Kesa look: the model '" + path + "' cannot be read; showing the missing model");
            entry.Done = true;
            _loading--;
            foreach (var ready in entry.Waiting) ready(entry.Template);
            entry.Waiting.Clear();
        }
    }
}
