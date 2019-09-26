using System;

namespace TwilightBoxart.Models.Base
{
    public class NoDbMatchException : Exception
    {
        public NoDbMatchException(string message)
            : base(message)
        {
        }
    }
}
