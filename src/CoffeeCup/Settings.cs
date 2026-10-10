using System;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CoffeeCup
{
    /// <summary>Per-user preferences, stored under HKCU\Software\CoffeeCup.</summary>
    internal sealed class Settings
    {
        private const string KeyPath = @"Software\CoffeeCup";
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValueName = "CoffeeCup";

        public bool KeepDisplayOn { get; set; } = true;
        public bool StartActive { get; set; }

        /// <summary>Length of a session started by double-clicking; 0 means indefinitely.</summary>
        public int DefaultMinutes { get; set; }

        /// <summary>Whether the "here's where I live" balloon has been shown once.</summary>
        public bool WelcomeShown { get; set; }

        public static Settings Load()
        {
            var s = new Settings();
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(KeyPath))
                {
                    if (key == null) return s;
                    s.KeepDisplayOn = ReadInt(key, nameof(KeepDisplayOn), 1) != 0;
                    s.StartActive = ReadInt(key, nameof(StartActive), 0) != 0;
                    s.DefaultMinutes = Math.Max(0, ReadInt(key, nameof(DefaultMinutes), 0));
                    s.WelcomeShown = ReadInt(key, nameof(WelcomeShown), 0) != 0;
                }
            }
            catch (Exception) { }
            return s;
        }

        public void Save()
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(KeyPath))
                {
                    key.SetValue(nameof(KeepDisplayOn), KeepDisplayOn ? 1 : 0, RegistryValueKind.DWord);
                    key.SetValue(nameof(StartActive), StartActive ? 1 : 0, RegistryValueKind.DWord);
                    key.SetValue(nameof(DefaultMinutes), DefaultMinutes, RegistryValueKind.DWord);
                    key.SetValue(nameof(WelcomeShown), WelcomeShown ? 1 : 0, RegistryValueKind.DWord);
                }
            }
            catch (Exception) { }
        }

        public static bool StartWithWindows
        {
            get
            {
                try
                {
                    using (var key = Registry.CurrentUser.OpenSubKey(RunKeyPath))
                        return key?.GetValue(RunValueName) is string;
                }
                catch (Exception) { return false; }
            }
            set
            {
                using (var key = Registry.CurrentUser.CreateSubKey(RunKeyPath))
                {
                    if (value)
                        key.SetValue(RunValueName, "\"" + Application.ExecutablePath + "\"");
                    else
                        key.DeleteValue(RunValueName, false);
                }
            }
        }

        /// <summary>True when the taskbar uses the light theme (Windows 10 1903+ / 11).</summary>
        public static bool TaskbarIsLight
        {
            get
            {
                try
                {
                    using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                        return ReadInt(key, "SystemUsesLightTheme", 0) != 0;
                }
                catch (Exception) { return false; }
            }
        }

        private static int ReadInt(RegistryKey key, string name, int fallback)
        {
            return key?.GetValue(name) is int i ? i : fallback;
        }
    }
}
