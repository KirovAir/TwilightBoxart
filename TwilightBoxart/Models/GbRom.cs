using KirovAir.Core.Extensions;

namespace TwilightBoxart.Models
{
    public class GbRom : Rom
    {
        public GbRom(byte[] header)
        {
            Title = header.GetString(308, 12);
            TitleId = header.GetString(320, 4);
        }
    }
}
