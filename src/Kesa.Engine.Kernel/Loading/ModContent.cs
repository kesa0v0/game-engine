using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Kesa.Engine.Loading
{
    /// <summary>엔진이 그 파일을 무엇으로 읽었나 (폴더 이름이 아니라 읽은 방식, spec-gaps G212).</summary>
    public enum ModFileKind
    {
        Manifest,
        Schema,
        Defs,
    }

    /// <summary>엔진이 읽은 모드 파일 하나의 지문 (spec-gaps G212).</summary>
    public sealed class ModFile
    {
        public ModFile(string path, ModFileKind kind, string hash)
        {
            Path = path;
            Kind = kind;
            Hash = hash;
        }

        /// <summary>모드 폴더 기준 상대 경로, 구분자 '/', NFC (G213).</summary>
        public string Path { get; }

        public ModFileKind Kind { get; }

        /// <summary>BOM을 빼고 줄바꿈을 LF로 맞춘 바이트의 SHA-256 앞 128비트, 16진수 32자.</summary>
        public string Hash { get; }

        /// <summary>저장과 진단에 쓰는 이름: <c>manifest</c>·<c>schema</c>·<c>defs</c>.</summary>
        public static string KindName(ModFileKind kind) => kind switch
        {
            ModFileKind.Manifest => "manifest",
            ModFileKind.Schema => "schema",
            _ => "defs",
        };
    }

    /// <summary>
    /// 로드한 모드 하나의 내용 지문 (spec S8 세이브 메타, spec-gaps G212). 엔진은 무엇이 바뀌어도 괜찮은지 판단하지 않는다:
    /// 파일별 지문과 전체 지문을 사실로 내고, 무엇을 무시할지는 사용처(멀티플레이 접속, 리플레이, 제보 도구)가 정한다.
    /// </summary>
    public sealed class ModContent
    {
        internal ModContent(string id, IReadOnlyList<ModFile> files)
        {
            Id = id;
            Files = files;
            Hash = ContentHash.OfMod(files);
        }

        public string Id { get; }

        /// <summary>파일 목록 전체의 지문. 목록이 같으면 같다.</summary>
        public string Hash { get; }

        /// <summary>엔진이 읽은 파일, 경로 서수 순.</summary>
        public IReadOnlyList<ModFile> Files { get; }
    }

    /// <summary>모드 내용 지문 계산 (spec-gaps G212). 함수 본문 해시(G144)와 같은 SHA-256 앞 128비트다.</summary>
    internal static class ContentHash
    {
        private const string ModTag = "kesa-mod-hash/1";

        /// <summary>맨 앞 UTF-8 BOM을 빼고 <c>\r\n</c>과 홀로 있는 <c>\r</c>을 <c>\n</c>으로 바꾼 바이트의 지문. 그 밖은 바꾸지 않는다.</summary>
        public static string OfFile(byte[] bytes)
        {
            int start = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
            var normalized = new MemoryStream(bytes.Length);
            for (int i = start; i < bytes.Length; i++)
            {
                byte b = bytes[i];
                if (b == (byte)'\r')
                {
                    normalized.WriteByte((byte)'\n');
                    if (i + 1 < bytes.Length && bytes[i + 1] == (byte)'\n') i++;
                    continue;
                }
                normalized.WriteByte(b);
            }
            return Digest(normalized.ToArray());
        }

        /// <summary>표시 뒤에 경로 서수 순으로 항목의 경로·종류·지문을, 각각 바이트 길이를 먼저 적어 이어 붙인 것의 지문.</summary>
        public static string OfMod(IReadOnlyList<ModFile> files)
        {
            var sorted = new List<ModFile>(files);
            sorted.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));
            var bytes = new MemoryStream();
            Text(bytes, ModTag);
            foreach (var f in sorted)
            {
                Text(bytes, f.Path);
                Text(bytes, ModFile.KindName(f.Kind));
                Text(bytes, f.Hash);
            }
            return Digest(bytes.ToArray());
        }

        private static void Text(MemoryStream bytes, string text)
        {
            var utf8 = Encoding.UTF8.GetBytes(text);
            var length = Encoding.ASCII.GetBytes(utf8.Length.ToString(CultureInfo.InvariantCulture) + ":");
            bytes.Write(length, 0, length.Length);
            bytes.Write(utf8, 0, utf8.Length);
        }

        private static string Digest(byte[] input)
        {
            byte[] digest;
            using (var sha = SHA256.Create()) digest = sha.ComputeHash(input);
            var hex = new StringBuilder(32);
            for (int i = 0; i < 16; i++) hex.Append(digest[i].ToString("x2", CultureInfo.InvariantCulture));
            return hex.ToString();
        }
    }
}
