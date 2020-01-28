using TwilightBoxart.Helpers;
using TwilightBoxart.Models.Base;

namespace TwilightBoxart.Models
{
    public class GbaRom : LibRetroRom
    {
        public override ConsoleType ConsoleType => Base.ConsoleType.GameBoyAdvance;

        public GbaRom(byte[] header)
        {
            Title = header.GetString(0xA0, 12);
            TitleId = header.GetString(0xAC, 4);
        }
    }
}
