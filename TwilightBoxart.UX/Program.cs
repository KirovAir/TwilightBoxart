using System;
using System.Net;
using System.Windows.Forms;

namespace TwilightBoxart.UX
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            // Possible windows XP / 7 / older .net framework compatibility fix.
            try
            {
                ServicePointManager.Expect100Continue = true;
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)48 // SSl3
                                                       | (SecurityProtocolType)192 // tls
                                                       | (SecurityProtocolType)768 // tls11
                                                       | (SecurityProtocolType)3072 // tls12
                                                       | (SecurityProtocolType)12288; // tls13
            }
            catch
            {
                try // Try without tls13
                {
                    ServicePointManager.SecurityProtocol = (SecurityProtocolType)48 // SSl3
                                                           | (SecurityProtocolType)192 // tls
                                                           | (SecurityProtocolType)768 // tls11
                                                           | (SecurityProtocolType)3072; // tls12
                }
                catch
                {
                    try // Try without tls12
                    {
                        ServicePointManager.SecurityProtocol = (SecurityProtocolType)48 // SSl3
                                                               | (SecurityProtocolType)192 // tls
                                                               | (SecurityProtocolType)768; // tls11
                    }
                    catch { }
                }
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
