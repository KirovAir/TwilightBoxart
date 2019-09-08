using System;
using System.IO;
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

        public static IRom FromFile(string filename)
        {
            // Determine type
            var ext = Path.GetExtension(filename).ToLower();
            if (ext == ".gb")
            {

            }

            using (var file = File.OpenRead(filename))
            using (var reader = new BinaryReader(file))
            {
                var header = reader.ReadBytes(328);

                if (header.ByteMatch(260, 0xCE, 0xED, 0x66, 0x66)) // Check for GB header.
                {
                    return new GbRom(header);
                }
                else if (header.ByteMatch(0, 0x2E, 0x00, 0x00, 0xEA)) // GBA (todo: fix)
                {
                    return new GbaRom(header);
                }
                else
                {
                    return new NdsRom(header);
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
