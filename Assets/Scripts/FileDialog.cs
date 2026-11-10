using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

// "Esporta" / "Importa" of the level editor: the Windows save / open dialog (comdlg32), so the player picks where the
// level file goes on their PC. Elsewhere there's no dialog: export writes to Documents/MiniPool, import is unavailable.
public static class FileDialog
{
    const string Filter = "Livello MiniPool (*.txt)\0*.txt\0Tutti i file\0*.*\0\0";

    /// <summary>Asks where to save `text` (suggested file name `name`.txt) and writes it; returns the path, null if cancelled.</summary>
    public static string Save(string name, string text)
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        var ofn = Dialog(name + ".txt", "Salva il livello sul PC");
        ofn.Flags = OverwritePrompt | PathMustExist | NoChangeDir;
        if (!GetSaveFileName(ofn)) return null;
        var path = ofn.lpstrFile.TrimEnd('\0');
        if (Path.GetExtension(path) == "") path += ".txt";
#else
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "MiniPool");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name + ".txt");
#endif
        File.WriteAllText(path, text);
        return path;
    }

    /// <summary>Asks for a level file to open; returns its path, null if cancelled (or no dialog on this platform).</summary>
    public static string Open()
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        var ofn = Dialog("", "Importa un livello");
        ofn.Flags = FileMustExist | PathMustExist | NoChangeDir;
        return GetOpenFileName(ofn) ? ofn.lpstrFile.TrimEnd('\0') : null;
#else
        return null;
#endif
    }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
    const int OverwritePrompt = 0x2, NoChangeDir = 0x8, PathMustExist = 0x800, FileMustExist = 0x1000;

    static OpenFileName Dialog(string file, string title)
    {
        var buffer = file + new string('\0', 1024 - file.Length); // the dialog writes the chosen path in here
        return new OpenFileName
        {
            hwndOwner = GetActiveWindow(),
            lpstrFilter = Filter,
            lpstrFile = buffer,
            nMaxFile = buffer.Length,
            lpstrTitle = title,
            lpstrDefExt = "txt",
            lpstrInitialDir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    class OpenFileName
    {
        public int lStructSize = Marshal.SizeOf(typeof(OpenFileName));
        public IntPtr hwndOwner, hInstance;
        public string lpstrFilter, lpstrCustomFilter;
        public int nMaxCustFilter, nFilterIndex;
        public string lpstrFile;
        public int nMaxFile;
        public string lpstrFileTitle;
        public int nMaxFileTitle;
        public string lpstrInitialDir, lpstrTitle;
        public int Flags;
        public short nFileOffset, nFileExtension;
        public string lpstrDefExt;
        public IntPtr lCustData, lpfnHook;
        public string lpTemplateName;
        public IntPtr pvReserved;
        public int dwReserved, FlagsEx;
    }

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode)]
    static extern bool GetSaveFileName([In, Out] OpenFileName ofn);

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode)]
    static extern bool GetOpenFileName([In, Out] OpenFileName ofn);

    [DllImport("user32.dll")]
    static extern IntPtr GetActiveWindow();
#endif
}
