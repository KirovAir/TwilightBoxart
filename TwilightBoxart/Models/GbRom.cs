using KirovAir.Core.Extensions;
using TwilightBoxart.Models.Base;

namespace TwilightBoxart.Models
{
    public class GbRom : LibRetroRom
    {
        public override ConsoleType ConsoleType => ConsoleType.Gb;

        public GbRom(byte[] header)
        {
            Title = header.GetString(0x134, 16);
            //TitleId = header.GetString(0x13F, 4);
        }
    }
}
