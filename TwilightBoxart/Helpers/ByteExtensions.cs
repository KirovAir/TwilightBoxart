using System;
using System.Collections.Generic;
using System.Text;

namespace KirovAir.Core.Extensions
{
    public static class ByteExtensions
    {
        public static bool ByteMatch(this byte[] array, int start, params byte[] bytes)
        {
            for (int i = 0; i < bytes.Length; i++)
            {
                if (start + i >= array.Length)
                    return false;

                if (array[start + i] != bytes[i])
                    return false;
            }

            return true;
        }

        public static string GetString(this byte[] array, int start, int length)
        {
            for (int i = 0; i < length; i++)
            {
                if (array[i + start] == '\0')
                {
                    length = i;
                    break;
                }
            }

            byte[] nameBytes = new byte[length];
            Buffer.BlockCopy(array, start, nameBytes, 0, nameBytes.Length);
            return Encoding.ASCII.GetString(nameBytes).Trim();
        }
    }
}
