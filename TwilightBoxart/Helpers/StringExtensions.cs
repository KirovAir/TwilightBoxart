using System.Linq;

namespace TwilightBoxart.Helpers
{
    public static class StringExtensions
    {
        public static string Truncate(this string value, int maxChars, bool addDots = true)
        {
            if (addDots)
                maxChars -= 2;
            return value.Length <= maxChars ? value : value.Substring(0, maxChars) + (addDots ? ".." : "");
        }

        public static string UpperToSpace(this string value)
        {
            return string.Concat(value.Select(x => char.IsUpper(x) ? " " + x : x.ToString())).TrimStart(' ');
        }
    }
}
