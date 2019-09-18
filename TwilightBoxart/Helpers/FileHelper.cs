using System.IO;
using System.IO.Compression;
using System.Reflection;

namespace KirovAir.Core.Utilities
{
    public static class FileHelper
    {
        public static byte[] Compress(Stream stream)
        {
            using (var mso = new MemoryStream())
            {
                using (var gs = new GZipStream(mso, CompressionMode.Compress))
                {
                    stream.CopyTo(gs);
                }
                return mso.ToArray();
            }
        }

        public static byte[] Decompress(Stream stream)
        {
            using (var mso = new MemoryStream())
            {
                using (var gs = new GZipStream(stream, CompressionMode.Decompress))
                {
                    gs.CopyTo(mso);
                }
                return mso.ToArray();
            }
        }
        
        public static string CombineUri(params string[] parts)
        {
            if (parts == null || parts.Length == 0) return string.Empty;
            if (parts.Length == 1)
                return parts[0];

            var res = parts[0];
            for (var i = 1; i < parts.Length; i++)
            {
                if (parts[i] == null)
                    continue;
                res = res.TrimEnd('/') + "/" + parts[i].TrimStart('/');
            }
            return res;
        }

        public static string GetCurrentDirectory()
        {
            return Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
        }
    }
}
