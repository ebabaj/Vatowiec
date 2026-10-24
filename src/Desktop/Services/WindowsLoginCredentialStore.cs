using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace Vatowiec.Desktop.Services;

public sealed class WindowsLoginCredentialStore : ILoginCredentialStore
{
    private const uint Generic = 1, LocalMachine = 2;
    private const int NotFound = 1168, MaximumBlob = 2560;
    private static LocalLoginStorageException Unavailable() => new("Windows Credential Manager jest niedostępny. Użyj logowania ręcznego; hasło nie zostanie zapisane w innym magazynie.");
    private static void CheckWindows() { if (!OperatingSystem.IsWindows()) throw Unavailable(); }

    // Presence checks inspect/clear the native buffer without creating a password string.
    public bool Contains(LoginTarget target) => target.UserId != null && ReadCredential(target, _ => true, false);
    public string? Read(LoginTarget target) => target.UserId == null ? null : ReadCredential(target,
        row => Marshal.PtrToStringUni(row.CredentialBlob, (int)row.CredentialBlobSize / 2), (string?)null);

    private static T ReadCredential<T>(LoginTarget target, Func<NativeCredential, T> use, T missing)
    {
        CheckWindows();
        if (!CredRead(target.CredentialName, Generic, 0, out var pointer))
        {
            if (Marshal.GetLastWin32Error() == NotFound) return missing;
            throw Unavailable();
        }
        NativeCredential row = default;
        try
        {
            row = Marshal.PtrToStructure<NativeCredential>(pointer);
            if (row.Type != Generic || row.CredentialBlob == IntPtr.Zero || row.CredentialBlobSize is 0 or > MaximumBlob ||
                row.CredentialBlobSize % 2 != 0 || !string.Equals(row.UserName, target.UserName, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(row.TargetName, target.CredentialName, StringComparison.Ordinal)) throw Unavailable();
            return use(row);
        }
        finally
        {
            if (row.CredentialBlob != IntPtr.Zero && row.CredentialBlobSize <= MaximumBlob)
                for (var i = 0; i < row.CredentialBlobSize; i++) Marshal.WriteByte(row.CredentialBlob, i, 0);
            CredFree(pointer);
        }
    }
    public void Write(LoginTarget target, string password)
    {
        CheckWindows();
        if (target.UserId == null) throw new LocalLoginStorageException("Najpierw potwierdź tożsamość użytkownika przez logowanie do aktualnego API.");
        if (string.IsNullOrEmpty(password) || password.Length > MaximumBlob / 2 || password.Contains('\0'))
            throw new LocalLoginStorageException("Nie można zapamiętać hasła o tej długości lub formacie. Nadal można logować się ręcznie.");
        var blob = Marshal.StringToCoTaskMemUni(password);
        try
        {
            var row = new NativeCredential
            {
                Type = Generic,
                TargetName = target.CredentialName,
                UserName = target.UserName,
                CredentialBlobSize = (uint)password.Length * 2,
                CredentialBlob = blob,
                Persist = LocalMachine
            };
            if (!CredWrite(ref row, 0)) throw Unavailable();
        }
        finally { Marshal.ZeroFreeCoTaskMemUnicode(blob); }
    }
    public void Delete(LoginTarget target)
    {
        CheckWindows();
        if (!CredDelete(target.CredentialName, Generic, 0) && Marshal.GetLastWin32Error() != NotFound) throw Unavailable();
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags, Type;
        [MarshalAs(UnmanagedType.LPWStr)] public string? TargetName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Comment;
        public FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist, AttributeCount;
        public IntPtr Attributes;
        [MarshalAs(UnmanagedType.LPWStr)] public string? TargetAlias;
        [MarshalAs(UnmanagedType.LPWStr)] public string? UserName;
    }
    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);
    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref NativeCredential credential, uint flags);
    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string target, uint type, uint flags);
    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr credential);
}
