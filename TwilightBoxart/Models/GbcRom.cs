using KirovAir.Core.Extensions;
using TwilightBoxart.Models.Base;

namespace TwilightBoxart.Models
{
    public class GbcRom : LibRetroRom
    {
        public override ConsoleType ConsoleType => ConsoleType.Gbc;

        public GbcRom(byte[] header)
        {
            Title = header.GetString(308, 12);
            TitleId = header.GetString(320, 4);
        }
    }
}
