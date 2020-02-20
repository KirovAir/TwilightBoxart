using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;

namespace TwilightBoxart
{
    public class FileMetaData
    {
        public string Filename { get; set; }
        public byte[] Header { get; set; }
        public string Sha1 { get; set; }
        public static FileMetaData FromFile(string filename, List<string> supportedExt = null)
        {
            using (var fs = File.OpenRead(filename))
            {
                var ext = Path.GetExtension(filename).ToLower();
                if (ext == ".zip")
                {
                    using (var archive = new ZipArchive(fs))
                    {
                        var romEntry = archive.Entries.FirstOrDefault(c => supportedExt == null || supportedExt.Contains(Path.GetExtension(c.Name)));
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

        public static FileMetaData FromStream(Stream stream, string filename)
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
                return new FileMetaData
                {
                    Filename = Path.GetFileName(filename),
                    Header = header,
                    Sha1 = hash
                };
            }
        }
    }
}
