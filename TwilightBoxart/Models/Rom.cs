using System;
using System.IO;
using System.Security.Cryptography;
using KirovAir.Core.Extensions;

namespace TwilightBoxart.Models
{
    /// <summary>
    /// Rom class. (For metadata)
    /// </summary>
    public class Rom : IRom
    {
        // NDS -> Name @ 0. Code @ 12
        // GBA -> Name @ 160. Code @ 172

        public byte[] Header { get; set; }
        public string Title { get; set; }
        public string TitleId { get; set; }
        public char RegionId { get; set; }
        public string Md5Hash { get; set; }

        public static IRom FromFile(string filename)
        {
            // Determine type
            using (var stream = File.OpenRead(filename))
            using (var md5 = MD5.Create())
            using (var reader = new BinaryReader(stream))
            {
                var hash = md5.ComputeHash(stream);
                var hashString = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant(); 

                stream.Seek(0, SeekOrigin.Begin);

                var header = reader.ReadBytes(328);
                IRom result = null;
                if (header.ByteMatch(260, 0xCE, 0xED, 0x66, 0x66)) // Check for GB header.
                {
                    result = new GbRom(header);
                }
                else if (header.ByteMatch(4, 0x24, 0xFF, 0xAE, 0x51)) // GBA 24 FF AE 51
                {
                    result = new GbaRom(header);
                }
                else if (header.ByteMatch(192, 0x24, 0xFF, 0xAE, 0x51)) //  24 FF AE 51
                {
                    result = new NdsRom(header);
                }
                if (result != null)
                {
                    result.Md5Hash = hashString;
                    return result;
                }
            }


            throw new Exception("Unknown ROM type.");
        }

        public virtual void DownloadBoxArt(string targetFile)
        {
            throw new NotImplementedException();
        }

        public override string ToString()
        {
            return $"{Title} - {TitleId} ({RegionId})";
        }
    }
}
