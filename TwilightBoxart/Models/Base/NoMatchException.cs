using System;

namespace TwilightBoxart.Models.Base
{
    public class NoMatchException : Exception
    {
        public NoMatchException(string message)
            : base(message)
        {
        }
    }
}
