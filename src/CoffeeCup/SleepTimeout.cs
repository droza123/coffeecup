using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace CoffeeCup
{
    /// <summary>
    /// Temporarily sets the active power plan's "sleep after" and "hibernate after" timeouts
    /// to Never. On Modern Standby PCs, Windows can idle into standby despite a power request,
    /// but it honours these timeouts. The original values are saved in the registry first, so
    /// they can be restored even after a crash (see <see cref="Restore"/> at startup).
    /// </summary>
    internal static class SleepTimeout
    {
        private static readonly Guid SleepSubgroup = new Guid("238C9FA8-0AAD-41ED-83F4-97BE242C8F20");
        private static readonly Guid StandbyIdle = new Guid("29F6C1DB-86DA-48C5-9FDB-F2B67B1F44DA");
        private static readonly Guid HibernateIdle = new Guid("9D7815A6-7EE4-497E-8888-515A05F02364");
        private const string SavedKeyPath = @"Software\CoffeeCup\SavedSleepSettings";
        private const string PolicyKeyPath = @"SOFTWARE\Policies\Microsoft\Power\PowerSettings\";

        private static readonly (string name, Guid setting, bool ac)[] Values =
        {
            ("StandbyAC", StandbyIdle, true),
            ("StandbyDC", StandbyIdle, false),
            ("HibernateAC", HibernateIdle, true),
            ("HibernateDC", HibernateIdle, false),
        };

        [DllImport("powrprof.dll")]
        private static extern uint PowerGetActiveScheme(IntPtr rootPowerKey, out IntPtr activePolicyGuid);

        [DllImport("powrprof.dll")]
        private static extern uint PowerSetActiveScheme(IntPtr rootPowerKey, ref Guid schemeGuid);

        [DllImport("powrprof.dll")]
        private static extern uint PowerReadACValueIndex(IntPtr rootPowerKey, ref Guid scheme, ref Guid subgroup, ref Guid setting, out uint value);

        [DllImport("powrprof.dll")]
        private static extern uint PowerReadDCValueIndex(IntPtr rootPowerKey, ref Guid scheme, ref Guid subgroup, ref Guid setting, out uint value);

        [DllImport("powrprof.dll")]
        private static extern uint PowerWriteACValueIndex(IntPtr rootPowerKey, ref Guid scheme, ref Guid subgroup, ref Guid setting, uint value);

        [DllImport("powrprof.dll")]
        private static extern uint PowerWriteDCValueIndex(IntPtr rootPowerKey, ref Guid scheme, ref Guid subgroup, ref Guid setting, uint value);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr mem);

        /// <summary>True when Group Policy fixes the sleep or hibernate timeout, so changing it has no effect.</summary>
        public static bool IsManagedByPolicy
        {
            get
            {
                try
                {
                    foreach (var setting in new[] { StandbyIdle, HibernateIdle })
                    {
                        using (var key = Registry.LocalMachine.OpenSubKey(PolicyKeyPath + setting.ToString("B").ToUpperInvariant()))
                        {
                            if (key?.GetValue("ACSettingIndex") != null || key?.GetValue("DCSettingIndex") != null)
                                return true;
                        }
                    }
                }
                catch (Exception) { }
                return false;
            }
        }

        /// <summary>Sets both timeouts to Never. Returns false if the power plan couldn't be changed.</summary>
        public static bool Override()
        {
            try
            {
                using (var existing = Registry.CurrentUser.OpenSubKey(SavedKeyPath))
                    if (existing != null) return true; // already overridden; keep the true originals

                if (!TryGetActiveScheme(out Guid scheme)) return false;

                var saved = new uint[Values.Length];
                for (int i = 0; i < Values.Length; i++)
                    if (!Read(scheme, Values[i].setting, Values[i].ac, out saved[i])) return false;

                // Save the originals before touching anything.
                using (var key = Registry.CurrentUser.CreateSubKey(SavedKeyPath))
                {
                    key.SetValue("Scheme", scheme.ToString());
                    for (int i = 0; i < Values.Length; i++)
                        key.SetValue(Values[i].name, unchecked((int)saved[i]), RegistryValueKind.DWord);
                }

                bool ok = true;
                foreach (var v in Values)
                    ok &= Write(scheme, v.setting, v.ac, 0);
                if (!ok)
                {
                    Restore();
                    return false;
                }
                PowerSetActiveScheme(IntPtr.Zero, ref scheme); // apply the change now
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Puts back the saved timeouts, if any. A value the user changed in the meantime
        /// (no longer Never) is left as they set it.
        /// </summary>
        public static void Restore()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(SavedKeyPath))
                {
                    if (key == null) return;
                    if (key.GetValue("Scheme") is string s && Guid.TryParse(s, out Guid scheme))
                    {
                        foreach (var v in Values)
                        {
                            if (!(key.GetValue(v.name) is int original)) continue;
                            if (Read(scheme, v.setting, v.ac, out uint current) && current == 0)
                                Write(scheme, v.setting, v.ac, unchecked((uint)original));
                        }
                        if (TryGetActiveScheme(out Guid active))
                            PowerSetActiveScheme(IntPtr.Zero, ref active); // re-apply whichever plan is active
                    }
                }
                Registry.CurrentUser.DeleteSubKeyTree(SavedKeyPath, false);
            }
            catch (Exception) { }
        }

        private static bool TryGetActiveScheme(out Guid scheme)
        {
            scheme = Guid.Empty;
            if (PowerGetActiveScheme(IntPtr.Zero, out IntPtr ptr) != 0 || ptr == IntPtr.Zero) return false;
            try
            {
                scheme = (Guid)Marshal.PtrToStructure(ptr, typeof(Guid));
                return true;
            }
            finally
            {
                LocalFree(ptr);
            }
        }

        private static bool Read(Guid scheme, Guid setting, bool ac, out uint value)
        {
            Guid sub = SleepSubgroup;
            return (ac
                ? PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, out value)
                : PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, out value)) == 0;
        }

        private static bool Write(Guid scheme, Guid setting, bool ac, uint value)
        {
            Guid sub = SleepSubgroup;
            return (ac
                ? PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, value)
                : PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, value)) == 0;
        }
    }
}
