using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using KirovAir.Core.Extensions;
using TwilightBoxart.Crawlers.NoIntro;

namespace TwilightBoxart.Models.Base
{
    /// <summary>
    /// Rom class. (For metadata)
    /// </summary>
    public class Rom : IRom
    {
        public byte[] Header { get; set; }
        public string Title { get; set; }
        public string TitleId { get; set; }
        public char RegionId { get; set; }
        public string Sha1 { get; set; }
        public virtual ConsoleType ConsoleType { get; set; }
        public string NoIntroName { get; set; }
        public NoIntroConsoleType NoIntroConsoleType { get; set; }

        public static IRom FromFile(string filename)
        {
            // Determine type
            using var stream = File.OpenRead(filename);
            using var sha1 = new SHA1Managed();
            using var reader = new BinaryReader(stream);

            var hash = sha1.ComputeHash(stream);
            var hashString = string.Concat(hash.Select(b => b.ToString("x2"))); // Hash it.
            stream.Seek(0, SeekOrigin.Begin);
            var header = reader.ReadBytes(328);
            IRom result = null;

            if (header.ByteMatch(260, 0xCE, 0xED, 0x66, 0x66)) // Check for GB header.
            {
                if (header.ByteMatch(0x143, 0x80) || header.ByteMatch(0x143, 0xC0))
                {
                    result = new GbcRom(header);
                }
                else
                {
                    result = new GbRom(header);
                }
            }
            else if (header.ByteMatch(4, 0x24, 0xFF, 0xAE, 0x51)) // GBA 24 FF AE 51
            {
                result = new GbaRom(header);
            }
            else if (header.ByteMatch(192, 0x24, 0xFF, 0xAE, 0x51)) //  24 FF AE 51
            {
                if (header[0x012] == 0x03)
                {
                    result = new DsiRom(header);
                }
                else
                {
                    result = new NdsRom(header);
                }
            }
            if (result != null)
            {
                result.Sha1 = hashString;
                return result;
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
