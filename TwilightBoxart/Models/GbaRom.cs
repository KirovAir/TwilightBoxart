using KirovAir.Core.Extensions;

namespace TwilightBoxart.Models
{
    public class GbaRom : Rom
    {
        public GbaRom(byte[] header)
        {
            Title = header.GetString(160, 12);
        }
    }
}
