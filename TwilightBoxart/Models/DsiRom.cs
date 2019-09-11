using TwilightBoxart.Models.Base;

namespace TwilightBoxart.Models
{
    public class DsiRom : NdsRom
    {
        public override ConsoleType ConsoleType => ConsoleType.Dsi;

        public DsiRom(byte[] header) : base(header)
        {
        }
    }
}
