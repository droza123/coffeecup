using System;
using System.Runtime.InteropServices;

namespace CoffeeCup
{
    /// <summary>Tells Windows whether the system (and optionally the display) must stay awake.</summary>
    internal static class Power
    {
        [Flags]
        private enum ExecutionState : uint
        {
            SystemRequired = 0x00000001,
            DisplayRequired = 0x00000002,
            Continuous = 0x80000000,
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern ExecutionState SetThreadExecutionState(ExecutionState flags);

        /// <summary>
        /// Must be called from the UI thread: the request lasts as long as the calling
        /// thread lives, or until it is called again.
        /// </summary>
        public static void KeepAwake(bool keepDisplayOn)
        {
            var flags = ExecutionState.Continuous | ExecutionState.SystemRequired;
            if (keepDisplayOn) flags |= ExecutionState.DisplayRequired;
            SetThreadExecutionState(flags);
        }

        public static void AllowSleep()
        {
            SetThreadExecutionState(ExecutionState.Continuous);
        }
    }
}
