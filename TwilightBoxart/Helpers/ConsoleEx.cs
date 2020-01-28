using System;
using System.Linq;

namespace TwilightBoxart.Helpers
{
    public static class ConsoleEx
    {
        public static ConsoleColor DefaultConsoleColor = Console.ForegroundColor;
        public static ConsoleColor DefaultMenuColor = Console.ForegroundColor;

        /// <summary>
        /// Clears the last written line(s) and resets the cursor.
        /// </summary>
        /// <param name="amount"></param>
        public static void ClearLine(int amount = 1)
        {
            Console.CursorTop -= amount;
            var position = Console.CursorTop;

            Console.Write(new string(' ', Console.WindowWidth * amount));
            Console.SetCursorPosition(0, position);
        }

        public static void WriteColor(string value, ConsoleColor color)
        {
            Console.ForegroundColor = color;
            Console.Write(value);
            Console.ResetColor();
        }

        public static void WriteColorLine(string value, ConsoleColor color)
        {
            WriteColor(value + Environment.NewLine, color);
        }

        public static void WriteColorLine(string value)
        {
            WriteColorLine(value, DefaultConsoleColor);
        }

        public static void WriteColor(string value)
        {
            WriteColor(value, DefaultConsoleColor);
        }

        public static void WriteRedLine(string value)
        {
            WriteColorLine(value, ConsoleColor.Red);
        }

        public static void WriteGreenLine(string value)
        {
            WriteColorLine(value, ConsoleColor.Green);
        }

        public static string ReadLine(string question = null)
        {
            if (question != null)
            {
                WriteColorLine(question);
            }

            return Console.ReadLine();
        }

        public static bool YesNoMenu(string question = null, bool reverseOrder = false)
        {
            var items = reverseOrder ? new[] { "No", "Yes" } : new[] { "Yes", "No" };
            return Menu(question, null, items) == "Yes";
        }

        public static TResult Menu<TResult>(string question = null, bool? verticalMenu = null) where TResult : struct, IConvertible
        {
            var items = ((TResult[])Enum.GetValues(typeof(TResult))).Select(c => c.GetDescription()).ToArray();
            var result = Menu(question, verticalMenu, items);
            return result.GetEnum<TResult>();
        }

        public static T Menu<T>(string question = null, bool? verticalMenu = null, params T[] answers)
        {
            return answers[MenuIndex(question, verticalMenu, answers)];
        }

        public static int MenuIndex<T>(string question = null, bool? verticalMenu = null, params T[] answers)
        {
            if (question != null)
            {
                WriteColorLine(question);
            }

            var vertical = verticalMenu.GetValueOrDefault();
            var index = 0;
            var maxLength = answers.Length >= Console.WindowHeight ? Console.WindowHeight - 1 : answers.Length;
            while (true)
            {
                var maxWidth = Console.WindowWidth - 2;
                if (!verticalMenu.HasValue)
                {
                    vertical = true;
                    if (string.Join(new string(' ', 5), answers).Length < maxWidth)
                    {
                        vertical = false;
                    }
                }

                for (var i = 0; i < maxLength; i++)
                {
                    Console.Write(i == index ? ">" : " ");
                    Console.Write(vertical ? $"{i + 1}. " : "");
                    WriteColor(answers[i].ToString().Truncate(vertical ? maxWidth - 4 : maxWidth), DefaultMenuColor);
                    Console.Write(i == index ? "<" : " ");

                    if (i == maxLength - 1) continue;

                    if (vertical)
                    {
                        Console.WriteLine();
                    }
                    else
                    {
                        Console.Write(" / ");
                    }
                }

                var input = Console.ReadKey();
                var key = input.Key;
                if (char.IsDigit(input.KeyChar))
                {
                    index = input.KeyChar - '0' - 1;
                }
                else
                {
                    switch (key)
                    {
                        case ConsoleKey.UpArrow:
                        case ConsoleKey.LeftArrow:
                            index--;
                            break;
                        case ConsoleKey.DownArrow:
                        case ConsoleKey.RightArrow:
                            index++;
                            break;
                    }
                }

                if (index < 0 || key == ConsoleKey.PageUp)
                    index = 0;
                if (index > maxLength - 1 || key == ConsoleKey.PageDown)
                    index = maxLength - 1;

                Console.WriteLine();

                if (key == ConsoleKey.Enter)
                    break;

                ClearLine(vertical ? maxLength : 1);
            }

            return index;
        }
    }
}
