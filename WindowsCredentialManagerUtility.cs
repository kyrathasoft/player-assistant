using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace PlayerAssistant
{
    internal sealed record StoredSecret(byte[] SecretBytes, DateTimeOffset LastWritten);

    internal interface IWindowsCredentialStoreBackend
    {
        bool TryRead(string targetName, out StoredSecret? storedSecret);
        void Write(string targetName, byte[] secretBytes, string? comment = null);
        void Delete(string targetName);
    }

    internal static class WindowsCredentialManagerUtility
    {
        private static readonly object BackendSyncRoot = new();
        private static IWindowsCredentialStoreBackend _backend = new WindowsCredentialStoreBackend();

        public static bool TryReadSecretUtf8(string targetName, out string? secret, out DateTimeOffset lastWritten)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(targetName);

            if (TryReadSecret(targetName, out var secretBytes, out lastWritten))
            {
                try
                {
                    secret = Encoding.UTF8.GetString(secretBytes);
                    return true;
                }
                finally
                {
                    if (secretBytes.Length > 0)
                    {
                        Array.Clear(secretBytes, 0, secretBytes.Length);
                    }
                }
            }

            secret = null;
            return false;
        }

        public static bool TryReadSecret(string targetName, out byte[] secretBytes, out DateTimeOffset lastWritten)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(targetName);

            var backend = GetBackend();
            if (backend.TryRead(targetName, out var storedSecret) && storedSecret is not null)
            {
                secretBytes = storedSecret.SecretBytes;
                lastWritten = storedSecret.LastWritten;
                return true;
            }

            secretBytes = [];
            lastWritten = default;
            return false;
        }

        public static void WriteSecretUtf8(string targetName, string secret, string? comment = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(targetName);
            ArgumentNullException.ThrowIfNull(secret);

            var secretBytes = Encoding.UTF8.GetBytes(secret);
            try
            {
                WriteSecret(targetName, secretBytes, comment);
            }
            finally
            {
                if (secretBytes.Length > 0)
                {
                    Array.Clear(secretBytes, 0, secretBytes.Length);
                }
            }
        }

        public static void WriteSecret(string targetName, byte[] secretBytes, string? comment = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(targetName);
            ArgumentNullException.ThrowIfNull(secretBytes);

            GetBackend().Write(targetName, secretBytes, comment);
        }

        public static void DeleteSecret(string targetName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(targetName);
            GetBackend().Delete(targetName);
        }

        internal static IDisposable PushBackendForTests(IWindowsCredentialStoreBackend backend)
        {
            ArgumentNullException.ThrowIfNull(backend);

            lock (BackendSyncRoot)
            {
                var previousBackend = _backend;
                _backend = backend;
                return new BackendScope(previousBackend);
            }
        }

        private static IWindowsCredentialStoreBackend GetBackend()
        {
            lock (BackendSyncRoot)
            {
                return _backend;
            }
        }

        private sealed class BackendScope : IDisposable
        {
            private readonly IWindowsCredentialStoreBackend _previousBackend;
            private bool _disposed;

            public BackendScope(IWindowsCredentialStoreBackend previousBackend)
            {
                _previousBackend = previousBackend;
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                lock (BackendSyncRoot)
                {
                    _backend = _previousBackend;
                }

                _disposed = true;
            }
        }

        private sealed class WindowsCredentialStoreBackend : IWindowsCredentialStoreBackend
        {
            private const int CredentialTypeGeneric = 1;
            private const int CredentialPersistLocalMachine = 2;
            private const int ErrorNotFound = 1168;

            public bool TryRead(string targetName, out StoredSecret? storedSecret)
            {
                if (!CredRead(targetName, CredentialTypeGeneric, 0, out var credentialPointer))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error == ErrorNotFound)
                    {
                        storedSecret = null;
                        return false;
                    }

                    throw new Win32Exception(error, $"Unable to read Windows Credential Manager secret '{targetName}'.");
                }

                try
                {
                    var credential = Marshal.PtrToStructure<CREDENTIAL>(credentialPointer);
                    var secretBytes = credential.CredentialBlobSize <= 0
                        ? []
                        : MarshalCredentialBlob(credential.CredentialBlob, credential.CredentialBlobSize);
                    storedSecret = new StoredSecret(secretBytes, FromFileTime(credential.LastWritten));
                    return true;
                }
                finally
                {
                    CredFree(credentialPointer);
                }
            }

            public void Write(string targetName, byte[] secretBytes, string? comment = null)
            {
                var blobPointer = IntPtr.Zero;
                try
                {
                    blobPointer = Marshal.AllocCoTaskMem(secretBytes.Length);
                    if (secretBytes.Length > 0)
                    {
                        Marshal.Copy(secretBytes, 0, blobPointer, secretBytes.Length);
                    }

                    var credential = new CREDENTIAL
                    {
                        Type = CredentialTypeGeneric,
                        TargetName = targetName,
                        Comment = comment,
                        Persist = CredentialPersistLocalMachine,
                        UserName = Environment.UserName,
                        CredentialBlobSize = secretBytes.Length,
                        CredentialBlob = blobPointer
                    };

                    if (!CredWrite(ref credential, 0))
                    {
                        throw new Win32Exception(
                            Marshal.GetLastWin32Error(),
                            $"Unable to write Windows Credential Manager secret '{targetName}'.");
                    }
                }
                finally
                {
                    if (blobPointer != IntPtr.Zero)
                    {
                        Marshal.FreeCoTaskMem(blobPointer);
                    }
                }
            }

            public void Delete(string targetName)
            {
                if (!CredDelete(targetName, CredentialTypeGeneric, 0))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error != ErrorNotFound)
                    {
                        throw new Win32Exception(error, $"Unable to delete Windows Credential Manager secret '{targetName}'.");
                    }
                }
            }

            private static byte[] MarshalCredentialBlob(IntPtr blobPointer, int blobSize)
            {
                var bytes = new byte[blobSize];
                Marshal.Copy(blobPointer, bytes, 0, blobSize);
                return bytes;
            }

            private static DateTimeOffset FromFileTime(Win32FileTime fileTime)
            {
                var rawFileTime = ((long)(uint)fileTime.dwHighDateTime << 32) | (uint)fileTime.dwLowDateTime;
                return DateTimeOffset.FromFileTime(rawFileTime);
            }

            [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "CredReadW", SetLastError = true)]
            private static extern bool CredRead(string target, int type, int reservedFlag, out IntPtr credentialPtr);

            [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "CredWriteW", SetLastError = true)]
            private static extern bool CredWrite(ref CREDENTIAL userCredential, int flags);

            [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "CredDeleteW", SetLastError = true)]
            private static extern bool CredDelete(string target, int type, int flags);

            [DllImport("advapi32.dll", SetLastError = false)]
            private static extern void CredFree(IntPtr credentialPtr);

            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
            private struct CREDENTIAL
            {
                public int Flags;
                public int Type;
                public string? TargetName;
                public string? Comment;
                public Win32FileTime LastWritten;
                public int CredentialBlobSize;
                public IntPtr CredentialBlob;
                public int Persist;
                public int AttributeCount;
                public IntPtr Attributes;
                public string? TargetAlias;
                public string? UserName;
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct Win32FileTime
            {
                public int dwLowDateTime;
                public int dwHighDateTime;
            }
        }
    }
}
