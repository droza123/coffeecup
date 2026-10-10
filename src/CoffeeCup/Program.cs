using System;
using System.Threading;
using System.Windows.Forms;

namespace CoffeeCup
{
    internal static class Program
    {
        internal const string MutexName = "CoffeeCup.SingleInstance";
        internal const string ExitEventName = "CoffeeCup.ExitRequest";
        internal const string ShowEventName = "CoffeeCup.ShowRequest";

        [STAThread]
        private static int Main(string[] args)
        {
            bool exitRequested = Array.Exists(args, a => a.Equals("--exit", StringComparison.OrdinalIgnoreCase));

            using (var mutex = new Mutex(true, MutexName, out bool firstInstance))
            {
                if (exitRequested)
                {
                    // Used by the installer and uninstaller to close a running copy cleanly.
                    if (firstInstance) return 0;
                    Signal(ExitEventName);
                    try { mutex.WaitOne(TimeSpan.FromSeconds(5)); }
                    catch (AbandonedMutexException) { }
                    return 0;
                }

                if (!firstInstance)
                {
                    // Already running: ask that copy to point out where it lives.
                    Signal(ShowEventName);
                    return 0;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                using (var app = new TrayApp())
                {
                    Application.Run(app);
                }
                mutex.ReleaseMutex();
                return 0;
            }
        }

        private static void Signal(string name)
        {
            try
            {
                using (var ev = EventWaitHandle.OpenExisting(name)) ev.Set();
            }
            catch (WaitHandleCannotBeOpenedException) { }
        }
    }
}
