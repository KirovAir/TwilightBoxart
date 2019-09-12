using TwilightBoxart.Models.Base;

namespace TwilightBoxart.Models
{
    /// <summary>
    /// Rom based on extension.
    /// </summary>
    public class UnknownRom : LibRetroRom
    {
        public override ConsoleType ConsoleType { get; set; }
    }
}