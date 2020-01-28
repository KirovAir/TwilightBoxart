using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
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
        public string SearchName
        {
            get
            {
                if (string.IsNullOrEmpty(FileName))
                    return null;

                var name = Path.GetFileNameWithoutExtension(FileName?.Replace(".lz77", ""));
                return name;
            }
        }

        internal ImgDownloader ImgDownloader { get; set; }

        public static IRom FromStream(Stream stream, string filename)
        {
            // Open file, hash it and determine type.
            using (var sha1 = new SHA1Managed())
            using (var reader = new BinaryReader(stream))
            {
                // Compute the hash, reset pointer.
                var hash = string.Concat(sha1.ComputeHash(stream).Select(b => b.ToString("x2")));
                stream.Seek(0, SeekOrigin.Begin);

                // This header is sufficient for most roms.
                var header = reader.ReadBytes(328);
                return FromMetadata(filename, hash, header);
            }
        }

        public static IRom FromMetadata(string filename, string sha1, byte[] header, string titleId = null)
        {
            IRom result = null;
            if (header != null && header.Length >= 328)
            {
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
            }

            if (result == null &&
                BoxartConfig.ExtensionMapping.TryGetValue(Path.GetExtension(filename)?.ToLower(), out var consoleType) &&
                consoleType != ConsoleType.Unknown)
            {
                if (titleId != null)
                {
                    result = DsiRom.IsDsiWare(titleId) ? new DsiRom(titleId) { ConsoleType = ConsoleType.NintendoDSi } : new NdsRom(titleId) { ConsoleType = ConsoleType.NintendoDS };
                }
                else
                {
                    // Backup mapper. Only supports sha1 matching.
                    result = new UnknownRom {ConsoleType = consoleType};
                }
            }

            if (result == null) throw new Exception("Unknown ROM type.");

            result.FileName = filename;
            result.Sha1 = sha1;
            return result;
        }

        public static IRom FromFile(string filename)
        {
            using (var fs = File.OpenRead(filename))
            {
                var ext = Path.GetExtension(filename).ToLower();
                if (ext == ".zip")
                {
                    using (var archive = new ZipArchive(fs))
                    {
                        var romEntry = archive.Entries.FirstOrDefault(c => BoxartConfig.ExtensionMapping.Keys.Contains(Path.GetExtension(c.Name)));
                        if (romEntry != null)
                        {
                            using (var ms = new MemoryStream())
                            using (var dec = romEntry.Open())
                            {
                                dec.CopyTo(ms);
                                ms.Seek(0, SeekOrigin.Begin);
                                return FromStream(ms, romEntry.FullName);
                            }
                        }
                    }
                }

                //if (filename.ToLower().Contains(".lz77."))
                //{
                //    fs.Seek(0, SeekOrigin.Begin);
                //    using (var decompressedFileStream = new MemoryStream())
                //    {
                //        using (DeflateStream decompressionStream = new DeflateStream(fs, CompressionMode.Decompress))
                //        {
                //            decompressionStream.CopyTo(decompressedFileStream);
                //        }

                //        decompressedFileStream.Seek(0, SeekOrigin.Begin);
                //        return FromStream(decompressedFileStream, filename);

                //    }
                //}

                return FromStream(fs, filename);
            }
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
