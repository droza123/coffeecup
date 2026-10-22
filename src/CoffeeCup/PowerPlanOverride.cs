using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace CoffeeCup
{
    /// <summary>
    /// Temporarily changes power plan settings while CoffeeCup is awake:
    /// "sleep after" and "hibernate after" become Never (on Modern Standby PCs Windows can idle
    /// into standby despite a power request) and, optionally, "when I close the lid" becomes
    /// Do nothing. The original values are saved in the registry first, so they can be
    /// restored even after a crash (see <see cref="Restore"/> at startup).
    /// </summary>
    internal static class PowerPlanOverride
    {
        private static readonly Guid SleepSubgroup = new Guid("238C9FA8-0AAD-41ED-83F4-97BE242C8F20");
        private static readonly Guid StandbyIdle = new Guid("29F6C1DB-86DA-48C5-9FDB-F2B67B1F44DA");
        private static readonly Guid HibernateIdle = new Guid("9D7815A6-7EE4-497E-8888-515A05F02364");
        private static readonly Guid ButtonsSubgroup = new Guid("4F971E89-EEBD-4455-A8DE-9E59040E7347");
        private static readonly Guid LidCloseAction = new Guid("5CA83367-6E45-459F-A27B-476B1D01C936");

        private const uint Never = 0;
        private const uint DoNothing = 0;

        // Key name kept from 1.0.2 so values saved by that version are still restored.
        private const string SavedKeyPath = @"Software\CoffeeCup\SavedSleepSettings";
        private const string PolicyKeyPath = @"SOFTWARE\Policies\Microsoft\Power\PowerSettings\";

        private sealed class Entry
        {
            public string Name;
            public Guid Subgroup, Setting;
            public bool Ac;
            public uint Value;
            public bool IsLid;
        }

        private static readonly Entry[] Entries =
        {
            new Entry { Name = "StandbyAC", Subgroup = SleepSubgroup, Setting = StandbyIdle, Ac = true, Value = Never },
            new Entry { Name = "StandbyDC", Subgroup = SleepSubgroup, Setting = StandbyIdle, Ac = false, Value = Never },
            new Entry { Name = "HibernateAC", Subgroup = SleepSubgroup, Setting = HibernateIdle, Ac = true, Value = Never },
            new Entry { Name = "HibernateDC", Subgroup = SleepSubgroup, Setting = HibernateIdle, Ac = false, Value = Never },
            new Entry { Name = "LidAC", Subgroup = ButtonsSubgroup, Setting = LidCloseAction, Ac = true, Value = DoNothing, IsLid = true },
            new Entry { Name = "LidDC", Subgroup = ButtonsSubgroup, Setting = LidCloseAction, Ac = false, Value = DoNothing, IsLid = true },
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

        /// <summary>True when Group Policy fixes one of the settings we change, so changing it has no effect.</summary>
        public static bool IsManagedByPolicy(bool includeLid)
        {
            try
            {
                var settings = new List<Guid> { StandbyIdle, HibernateIdle };
                if (includeLid) settings.Add(LidCloseAction);
                foreach (var setting in settings)
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

        /// <summary>
        /// Applies the overrides. Returns false if the power plan couldn't be changed.
        /// Call <see cref="Restore"/> first to switch between including the lid or not.
        /// </summary>
        public static bool Override(bool includeLid)
        {
            try
            {
                using (var existing = Registry.CurrentUser.OpenSubKey(SavedKeyPath))
                    if (existing != null) return true; // already overridden; keep the true originals

                if (!TryGetActiveScheme(out Guid scheme)) return false;

                // Read originals. A setting the PC doesn't have (e.g. no lid on a desktop) is skipped.
                var toChange = new List<(Entry entry, uint original)>();
                foreach (var e in Entries)
                {
                    if (e.IsLid && !includeLid) continue;
                    if (Read(scheme, e, out uint original))
                        toChange.Add((e, original));
                    else if (!e.IsLid)
                        return false; // the sleep timeouts are essential
                }

                // Save the originals before touching anything.
                using (var key = Registry.CurrentUser.CreateSubKey(SavedKeyPath))
                {
                    key.SetValue("Scheme", scheme.ToString());
                    foreach (var (e, original) in toChange)
                        key.SetValue(e.Name, unchecked((int)original), RegistryValueKind.DWord);
                }

                bool ok = true;
                foreach (var (e, _) in toChange)
                    ok &= Write(scheme, e, e.Value);
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
        /// Puts back the saved values, if any. A value the user changed in the meantime
        /// (no longer what CoffeeCup set) is left as they set it.
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
                        foreach (var e in Entries)
                        {
                            if (!(key.GetValue(e.Name) is int original)) continue;
                            if (Read(scheme, e, out uint current) && current == e.Value)
                                Write(scheme, e, unchecked((uint)original));
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

        private static bool Read(Guid scheme, Entry e, out uint value)
        {
            Guid sub = e.Subgroup, setting = e.Setting;
            return (e.Ac
                ? PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, out value)
                : PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, out value)) == 0;
        }

        private static bool Write(Guid scheme, Entry e, uint value)
        {
            Guid sub = e.Subgroup, setting = e.Setting;
            return (e.Ac
                ? PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, value)
                : PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, value)) == 0;
        }
    }
}
