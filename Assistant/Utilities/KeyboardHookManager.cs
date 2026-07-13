using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace Assistant.Utilities
{
    public static class KeyboardHookManager
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYUP = 0x0105;

        private static LowLevelKeyboardProc? _proc;
        private static IntPtr _hookID = IntPtr.Zero;

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        private static extern short GetKeyState(int nVirtKey);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        // VirtKey Constants
        private const int VK_OEM_3 = 0xC0; // ~ or ` key
        private const int VK_T = 0x54;
        private const int VK_CONTROL = 0x11;
        private const int VK_MENU = 0x12; // Alt
        private const int VK_SHIFT = 0x10;
        private const int VK_LWIN = 0x5B;
        private const int VK_RWIN = 0x5C;

        [StructLayout(LayoutKind.Sequential)]
        struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct HARDWAREINPUT
        {
            public uint uMsg;
            public ushort wParamL;
            public ushort wParamH;
        }

        [StructLayout(LayoutKind.Explicit)]
        struct InputUnion
        {
            [FieldOffset(0)]
            public KEYBDINPUT ki;
            [FieldOffset(0)]
            public MOUSEINPUT mi;
            [FieldOffset(0)]
            public HARDWAREINPUT hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct INPUT
        {
            public uint type;
            public InputUnion U;
        }

        private const int INPUT_KEYBOARD = 1;
        private const uint KEYEVENTF_KEYUP = 0x0002;

        // Settings
        public static bool BindTildeToT { get; set; } = false;
        public static bool HotkeyEnabled { get; set; } = true;
        public static int ShortcutVirtualKey { get; set; } = VK_T;
        public static bool ShortcutCtrl { get; set; } = true;
        public static bool ShortcutAlt { get; set; } = false;
        public static bool ShortcutShift { get; set; } = false;

        public static Action? OnShortcutPressed { get; set; }

        public static void Start()
        {
            if (_hookID == IntPtr.Zero)
            {
                // Store delegate reference to prevent garbage collection
                _proc = HookCallback;
                _hookID = SetHook(_proc);
            }
        }

        public static void Stop()
        {
            if (_hookID != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookID);
                _hookID = IntPtr.Zero;
                _proc = null;
            }
        }

        private static IntPtr SetHook(LowLevelKeyboardProc proc)
        {
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule!)
            {
                return SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(curModule.ModuleName), 0);
            }
        }

        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int vkCode = Marshal.ReadInt32(lParam);
                int eventType = wParam.ToInt32();

                bool isKeyDown = eventType == WM_KEYDOWN || eventType == WM_SYSKEYDOWN;
                bool isKeyUp = eventType == WM_KEYUP || eventType == WM_SYSKEYUP;

                // 1. Remap tilde (~) to T
                if (BindTildeToT && vkCode == VK_OEM_3)
                {
                    if (isKeyDown)
                    {
                        // Simulate pressing T
                        SendKey(VK_T, true);
                        SendKey(VK_T, false);
                    }
                    // Suppress tilde key
                    return (IntPtr)1;
                }

                // 2. Custom hotkey check
                if (HotkeyEnabled && vkCode == ShortcutVirtualKey)
                {
                    // Check modifiers state
                    bool ctrlPressed = (GetKeyState(VK_CONTROL) & 0x8000) != 0;
                    bool altPressed = (GetKeyState(VK_MENU) & 0x8000) != 0;
                    bool shiftPressed = (GetKeyState(VK_SHIFT) & 0x8000) != 0;

                    if (ctrlPressed == ShortcutCtrl && altPressed == ShortcutAlt && shiftPressed == ShortcutShift)
                    {
                        if (isKeyDown)
                        {
                            // Trigger callback asynchronously
                            OnShortcutPressed?.Invoke();
                        }
                        // Suppress key combination
                        return (IntPtr)1;
                    }
                }
            }

            return CallNextHookEx(_hookID, nCode, wParam, lParam);
        }

        private static void SendKey(ushort vkCode, bool down)
        {
            INPUT[] inputs = new INPUT[1];
            inputs[0] = new INPUT
            {
                type = INPUT_KEYBOARD,
                U = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = vkCode,
                        wScan = 0,
                        dwFlags = down ? 0 : KEYEVENTF_KEYUP,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            };

            SendInput(1, inputs, Marshal.SizeOf(typeof(INPUT)));
        }

        public static void SimulateCopy()
        {
            SendKey(VK_CONTROL, true);
            SendKey(0x43, true); // C
            Thread.Sleep(20);
            SendKey(0x43, false);
            SendKey(VK_CONTROL, false);
        }

        public static void SimulateSelectAllAndCopy()
        {
            SendKey(VK_CONTROL, true);
            SendKey(0x41, true); // A
            Thread.Sleep(20);
            SendKey(0x41, false);
            SendKey(VK_CONTROL, false);

            Thread.Sleep(50);

            SendKey(VK_CONTROL, true);
            SendKey(0x43, true); // C
            Thread.Sleep(20);
            SendKey(0x43, false);
            SendKey(VK_CONTROL, false);
        }

        public static void SimulatePaste()
        {
            SendKey(VK_CONTROL, true);
            SendKey(0x56, true); // V
            Thread.Sleep(20);
            SendKey(0x56, false);
            SendKey(VK_CONTROL, false);
        }

        // Helper to parse hotkey string, e.g., "Ctrl+T" or "Ctrl+Alt+S"
        public static void ParseShortcutString(string shortcutStr)
        {
            ShortcutCtrl = false;
            ShortcutAlt = false;
            ShortcutShift = false;
            ShortcutVirtualKey = VK_T; // Fallback default

            if (string.IsNullOrWhiteSpace(shortcutStr))
                return;

            string[] parts = shortcutStr.Split('+');
            foreach (var part in parts)
            {
                string trimmed = part.Trim().ToLower();
                if (trimmed == "ctrl" || trimmed == "control")
                {
                    ShortcutCtrl = true;
                }
                else if (trimmed == "alt")
                {
                    ShortcutAlt = true;
                }
                else if (trimmed == "shift")
                {
                    ShortcutShift = true;
                }
                else
                {
                    ShortcutVirtualKey = GetVirtualKeyCode(trimmed);
                }
            }
        }

        private static int GetVirtualKeyCode(string keyName)
        {
            if (string.IsNullOrWhiteSpace(keyName)) return 0;
            keyName = keyName.Trim().ToUpper();

            if (keyName.Length == 1 && keyName[0] >= 'A' && keyName[0] <= 'Z')
            {
                return keyName[0];
            }

            if (keyName.Length == 1 && keyName[0] >= '0' && keyName[0] <= '9')
            {
                return keyName[0];
            }

            if (keyName.StartsWith("F") && keyName.Length > 1 && int.TryParse(keyName.Substring(1), out int fNum) && fNum >= 1 && fNum <= 12)
            {
                return 0x6F + fNum; // F1 is 0x70
            }

            switch (keyName)
            {
                case "SPACE": return 0x20;
                case "TAB": return 0x09;
                case "ENTER": return 0x0D;
                case "RETURN": return 0x0D;
                case "ESC": return 0x1B;
                case "ESCAPE": return 0x1B;
                case "BACK": return 0x08;
                case "BACKSPACE": return 0x08;
                case "INSERT": return 0x2D;
                case "DELETE": return 0x2E;
                case "DEL": return 0x2E;
                case "HOME": return 0x24;
                case "END": return 0x23;
                case "PAGEUP": return 0x21;
                case "PAGEDOWN": return 0x22;
                case "UP": return 0x26;
                case "DOWN": return 0x28;
                case "LEFT": return 0x25;
                case "RIGHT": return 0x27;
                case "OEM_3": return VK_OEM_3;
                case "~": return VK_OEM_3;
                case "`": return VK_OEM_3;
                default:
                    // Try parsing as integer virtual key code
                    if (int.TryParse(keyName, out int parsedVal))
                        return parsedVal;
                    return 0;
            }
        }
    }
}
