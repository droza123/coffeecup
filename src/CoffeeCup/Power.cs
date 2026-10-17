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

        // ---- Simulated activity ------------------------------------------------------
        //
        // Power requests alone aren't always honoured: managed PCs can disable them by
        // policy, and Modern Standby laptops may still go idle. Like the original Caffeine,
        // we also press F15 (a key almost nothing listens to) when the user has been idle
        // for a while, which resets Windows' idle timers.

        private const ushort VkF15 = 0x7E;
        private const uint InputKeyboard = 1;
        private const uint KeyEventKeyUp = 0x0002;

        [StructLayout(LayoutKind.Sequential)]
        private struct LastInputInfo
        {
            public uint cbSize;
            public uint dwTime;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MouseInput
        {
            public int dx, dy;
            public uint mouseData, dwFlags, time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KeybdInput
        {
            public ushort wVk, wScan;
            public uint dwFlags, time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)] public MouseInput mi; // largest member, sets the union's size
            [FieldOffset(0)] public KeybdInput ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Input
        {
            public uint type;
            public InputUnion u;
        }

        [DllImport("user32.dll")]
        private static extern bool GetLastInputInfo(ref LastInputInfo info);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint count, Input[] inputs, int size);

        /// <summary>Time since the last keyboard or mouse input in this session.</summary>
        public static TimeSpan IdleTime
        {
            get
            {
                var info = new LastInputInfo { cbSize = (uint)Marshal.SizeOf(typeof(LastInputInfo)) };
                if (!GetLastInputInfo(ref info)) return TimeSpan.Zero;
                uint idleMs = unchecked((uint)Environment.TickCount - info.dwTime);
                return TimeSpan.FromMilliseconds(idleMs);
            }
        }

        /// <summary>Taps F15. Fails harmlessly (e.g. on the lock screen).</summary>
        public static void SimulateActivity()
        {
            var inputs = new[]
            {
                new Input { type = InputKeyboard, u = new InputUnion { ki = new KeybdInput { wVk = VkF15 } } },
                new Input { type = InputKeyboard, u = new InputUnion { ki = new KeybdInput { wVk = VkF15, dwFlags = KeyEventKeyUp } } },
            };
            SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(Input)));
        }
    }
}
