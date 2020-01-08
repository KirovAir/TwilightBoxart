using System.Diagnostics;
using System.Runtime.InteropServices;

namespace KirovAir.Core.Utilities
{
    public static class OSHelper
    {
        public static bool IsWindows() => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        public static bool IsMacOS() => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

        public static bool IsLinux() => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

        public static void OpenBrowser(string url)
        {
            try
            {
                Process.Start(url);
            }
            catch
            {
                // hack because of this: https://github.com/dotnet/corefx/issues/10361
                if (OSHelper.IsWindows())
                {
                    url = url.Replace("&", "^&");
                    Process.Start(new ProcessStartInfo("cmd", $"/c start {url}") { CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Minimized });
                }
                else if (OSHelper.IsLinux())
                {
                    Process.Start("xdg-open", url);
                }
                else if (OSHelper.IsMacOS())
                {
                    Process.Start("open", url);
                }
                else
                {
                    throw;
                }
            }
        }
    }
}