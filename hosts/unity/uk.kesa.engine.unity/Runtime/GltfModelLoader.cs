#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using GLTFast;
using UnityEngine;

namespace Kesa.Engine.Unity
{
    /// <summary>glTF(<c>.glb</c>·<c>.gltf</c>)를 glTFast로 읽는다 (G391·G433). 모드 폴더의 파일이면 경로로 읽어 따로 둔 버퍼·텍스처도 찾는다.</summary>
    public sealed class GltfModelLoader : IModelLoader
    {
        // 원본이 쓰는 메시·재질은 가져온 것이 들고 있으므로 원본이 사는 동안 놓지 않는다
        private readonly List<GltfImport> _imports = new List<GltfImport>();

        public IReadOnlyList<string> Extensions { get; } = new[] { ".glb", ".gltf" };

        public async Task<GameObject?> Load(string path, IUiAssets assets, Transform holder)
        {
            // 에디터(확인)에서는 프레임 예산을 나눌 MonoBehaviour가 없으므로 끊지 않고 읽는다
            var gltf = new GltfImport(deferAgent: Application.isPlaying ? null : new UninterruptedDeferAgent());
            bool loaded;
            if (assets is ModFolderAssets folders && folders.Find(path) is string file)
                loaded = await gltf.Load(new Uri(file));
            else
            {
                byte[]? bytes = assets.ReadBytes(path);
                if (bytes == null) return null;
                loaded = path.EndsWith(".glb", StringComparison.OrdinalIgnoreCase)
                    ? await gltf.LoadGltfBinary(bytes)
                    : await gltf.LoadGltfJson(Encoding.UTF8.GetString(bytes));
            }
            if (!loaded)
            {
                gltf.Dispose();
                return null;
            }
            var root = new GameObject(path);
            root.SetActive(false);
            root.transform.SetParent(holder, false);
            if (!await gltf.InstantiateMainSceneAsync(root.transform))
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(root);
                else UnityEngine.Object.DestroyImmediate(root);
                gltf.Dispose();
                return null;
            }
            _imports.Add(gltf);
            return root;
        }
    }
}
