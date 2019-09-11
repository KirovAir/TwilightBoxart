using KirovAir.Core.Extensions;
using TwilightBoxart.Models.Base;

namespace TwilightBoxart.Models
{
    public class SnesRom : LibRetroRom
    {
        public override ConsoleType ConsoleType => ConsoleType.Snes;

        public SnesRom(byte[] header)
        {
            Title = header.GetString(0xA0, 12);
            TitleId = header.GetString(0xAC, 4);
        }
    }
}
