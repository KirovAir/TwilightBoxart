using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using KirovAir.Core.Extensions;
using TwilightBoxart.Helpers;

namespace TwilightBoxart.Models.Base
{
    /// <summary>
    /// Rom class. (For metadata)
    /// </summary>
    public class Rom : IRom
    {
        public string FileName { get; set; }
        public string Title { get; set; }
        public string TitleId { get; set; }
        public string Sha1 { get; set; }
        public virtual ConsoleType ConsoleType { get; set; }
        public string NoIntroName { get; set; }
        public ConsoleType NoIntroConsoleType { get; set; }
        internal ImgDownloader ImgDownloader { get; set; }

        public static IRom FromStream(Stream stream, string filename)
        {
            // Open file, hash it and determine type.
            using var sha1 = new SHA1Managed();
            using var reader = new BinaryReader(stream);

            // Compute the hash, reset pointer.
            var hash = sha1.ComputeHash(stream);
            stream.Seek(0, SeekOrigin.Begin);

            // This header is sufficient for most roms.
            var header = reader.ReadBytes(328);
            IRom result = null;

            if (header.ByteMatch(0x104, 0xCE, 0xED, 0x66, 0x66) || header.ByteMatch(0x100, 0x00, 0xC3, 0x50, 0x01) || header.ByteMatch(0x104, 0x11, 0x23, 0xF1, 0x1E)) // Check for GB header. (Headers??)
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

            if (result == null && BoxartConfig.ExtensionMapping.TryGetValue(Path.GetExtension(filename), out var consoleType))
            {
                // Backup mapper. Only supports sha1 matching.
                result = new UnknownRom {ConsoleType = consoleType};
            }

            if (result == null) throw new Exception("Unknown ROM type.");

            result.FileName = filename;
            result.Sha1 = string.Concat(hash.Select(b => b.ToString("x2"))); // Hash it.

            return result;

        }

        public static IRom FromFile(string filename)
        {
            using var stream = File.OpenRead(filename);
            return FromStream(stream, filename);
        }

        public virtual void DownloadBoxArt(string targetFile)
        {
            throw new NotImplementedException();
        }

        public void SetDownloader(ImgDownloader downloader)
        {
            ImgDownloader = downloader;
        }

        public override string ToString()
        {
            return $"{Title} - {TitleId}";
        }
    }
}
