using System;
using System.IO;
using System.Runtime.InteropServices;

namespace HalliGalli.Movement
{
    // Win32 common dialogs work in the Windows player without Windows Forms or editor assemblies.
    public static class UserMapStudioFileDialog
    {
        public static bool IsOpen { get; private set; }
        public static string OpenMapFile(string directory) => Show(false, directory, null);
        public static string SaveMapFile(string directory, string name) => Show(true, directory, name);

        static string Show(bool save, string directory, string name)
        {
            if (IsOpen) return null;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            Directory.CreateDirectory(directory);
            string initialFile = "";
            if (save)
            {
                string safeName = string.IsNullOrWhiteSpace(name) ? "내 맵" : name.Trim();
                foreach (char invalid in Path.GetInvalidFileNameChars()) safeName = safeName.Replace(invalid, '_');
                safeName = safeName.TrimEnd('.', ' ');
                if (safeName.Length == 0) safeName = "내 맵";
                initialFile = safeName + UserMapStorage.Extension;
            }
            const int capacity = 32768;
            IntPtr file = Marshal.StringToHGlobalUni(initialFile.PadRight(capacity - 1, '\0'));
            var dialog = new OpenFileName
            {
                size = Marshal.SizeOf(typeof(OpenFileName)),
                owner = GetActiveWindow(),
                filter = "Bell Run 맵 (*.bellmap)\0*.bellmap\0\0",
                filterIndex = 1,
                file = file,
                maxFile = capacity,
                initialDirectory = directory,
                title = save ? "맵 내보내기 · .bellmap 파일 하나로 전달하세요" : "맵 가져오기 · .bellmap 파일 선택",
                defaultExtension = "bellmap",
                flags = Explorer | NoChangeDirectory | PathMustExist | (save ? OverwritePrompt | NoReadOnlyReturn : FileMustExist)
            };
            IsOpen = true;
            try
            {
                bool result = save ? GetSaveFileName(ref dialog) : GetOpenFileName(ref dialog);
                if (result) return Marshal.PtrToStringUni(file);
                uint error = CommDlgExtendedError();
                if (error != 0) throw new IOException("Windows 파일 선택 창을 열지 못했어요. 오류: 0x" + error.ToString("X"));
                return null;
            }
            finally { IsOpen = false; Marshal.FreeHGlobal(file); }
#else
            throw new PlatformNotSupportedException("파일 가져오기/내보내기는 Windows 게임에서 지원해요.");
#endif
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        const int OverwritePrompt = 0x00000002;
        const int NoChangeDirectory = 0x00000008;
        const int PathMustExist = 0x00000800;
        const int FileMustExist = 0x00001000;
        const int NoReadOnlyReturn = 0x00008000;
        const int Explorer = 0x00080000;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct OpenFileName
        {
            public int size;
            public IntPtr owner, instance;
            [MarshalAs(UnmanagedType.LPWStr)] public string filter;
            public IntPtr customFilter;
            public int maxCustomFilter, filterIndex;
            public IntPtr file;
            public int maxFile;
            public IntPtr fileTitle;
            public int maxFileTitle;
            [MarshalAs(UnmanagedType.LPWStr)] public string initialDirectory;
            [MarshalAs(UnmanagedType.LPWStr)] public string title;
            public int flags;
            public short fileOffset, fileExtension;
            [MarshalAs(UnmanagedType.LPWStr)] public string defaultExtension;
            public IntPtr customData, hook;
            [MarshalAs(UnmanagedType.LPWStr)] public string templateName;
            public IntPtr reserved;
            public int reservedSize, extendedFlags;
        }

        [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool GetOpenFileName(ref OpenFileName dialog);
        [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool GetSaveFileName(ref OpenFileName dialog);
        [DllImport("comdlg32.dll")]
        static extern uint CommDlgExtendedError();
        [DllImport("user32.dll")]
        static extern IntPtr GetActiveWindow();
#endif
    }
}
