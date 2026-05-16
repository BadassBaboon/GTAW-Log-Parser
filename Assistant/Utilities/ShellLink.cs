using System;
using System.Runtime.InteropServices;
using System.Text;
using FILETIME = System.Runtime.InteropServices.ComTypes.FILETIME;
using IPersistFile = System.Runtime.InteropServices.ComTypes.IPersistFile;

namespace Assistant.Utilities
{
    // Managed wrapper around IShellLinkW + IPersistFile so we can create, read
    // and update .lnk shortcuts without an IWshRuntimeLibrary COM reference
    // (which blocks dotnet CLI builds).
    internal sealed class ShellLink : IDisposable
    {
        private const int MaxPath = 260;
        private const uint SLGP_UNCPRIORITY = 0x0002;

        private IShellLinkW _link;

        public ShellLink()
        {
            _link = (IShellLinkW)new CShellLink();
        }

        public string TargetPath
        {
            get
            {
                StringBuilder sb = new StringBuilder(MaxPath);
                WIN32_FIND_DATAW find = default;
                _link.GetPath(sb, sb.Capacity, ref find, SLGP_UNCPRIORITY);
                return sb.ToString();
            }
            set => _link.SetPath(value);
        }

        public string Arguments
        {
            get
            {
                StringBuilder sb = new StringBuilder(1024);
                _link.GetArguments(sb, sb.Capacity);
                return sb.ToString();
            }
            set => _link.SetArguments(value);
        }

        public string WorkingDirectory
        {
            get
            {
                StringBuilder sb = new StringBuilder(MaxPath);
                _link.GetWorkingDirectory(sb, sb.Capacity);
                return sb.ToString();
            }
            set => _link.SetWorkingDirectory(value);
        }

        public void Load(string path)
        {
            ((IPersistFile)_link).Load(path, 0);
        }

        public void Save(string path)
        {
            ((IPersistFile)_link).Save(path, true);
        }

        public void Dispose()
        {
            if (_link != null)
            {
                Marshal.FinalReleaseComObject(_link);
                _link = null;
            }
        }

        [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
        private class CShellLink { }

        [ComImport,
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown),
         Guid("000214F9-0000-0000-C000-000000000046")]
        private interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath,
                ref WIN32_FIND_DATAW pfd, uint fFlags);
            void GetIDList(out IntPtr ppidl);
            void SetIDList(IntPtr pidl);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
            void GetHotkey(out short pwHotkey);
            void SetHotkey(short wHotkey);
            void GetShowCmd(out int piShowCmd);
            void SetShowCmd(int iShowCmd);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath,
                int cchIconPath, out int piIcon);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
            void Resolve(IntPtr hwnd, uint fFlags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WIN32_FIND_DATAW
        {
            public uint dwFileAttributes;
            public FILETIME ftCreationTime;
            public FILETIME ftLastAccessTime;
            public FILETIME ftLastWriteTime;
            public uint nFileSizeHigh;
            public uint nFileSizeLow;
            public uint dwReserved0;
            public uint dwReserved1;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string cFileName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
            public string cAlternateFileName;
        }
    }
}
