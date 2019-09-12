using KirovAir.Core.Extensions;
using TwilightBoxart.Models.Base;

namespace TwilightBoxart.Models
{
    public class GbcRom : LibRetroRom
    {
        public override ConsoleType ConsoleType => Base.ConsoleType.GameBoyColor;

        public GbcRom(byte[] header)
        {
            var isColorOnly = header[0x143] == 0xC0;

            if (isColorOnly)
            {
                Title = header.GetString(0x134, 11);
                TitleId = header.GetString(0x13F, 4);
            }
            else
            {
                Title = header.GetString(0x134, 15);
            }
        }
    }
}
