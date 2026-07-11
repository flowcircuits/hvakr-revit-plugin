using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace HVAKR.Api.Updates;

public interface IUpdateSignatureVerifier
{
    void Verify(string path);
}

public sealed class AuthenticodeVerifier : IUpdateSignatureVerifier
{
    private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");

    public void Verify(string path)
    {
        using var fileInfo = new WinTrustFileInfo(path);
        using var trustData = new WinTrustData(fileInfo.Pointer);
        var action = GenericVerifyV2;
        var result = WinVerifyTrust(IntPtr.Zero, ref action, trustData.Pointer);
        if (result != 0)
            throw new InvalidDataException($"The update installer signature is not trusted (0x{result:X8}).");

        using var certificate = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
        if (!certificate.Subject.Contains("O=Flow Circuits", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"The update installer publisher is not Flow Circuits: {certificate.Subject}");
    }

    [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = true)]
    private static extern uint WinVerifyTrust(IntPtr hwnd, ref Guid actionId, IntPtr trustData);

    private sealed class WinTrustFileInfo : IDisposable
    {
        public IntPtr Pointer { get; }
        private readonly IntPtr _filePathPointer;

        public WinTrustFileInfo(string path)
        {
            _filePathPointer = Marshal.StringToCoTaskMemUni(path);
            var data = new NativeFileInfo
            {
                StructSize = (uint)Marshal.SizeOf<NativeFileInfo>(),
                FilePath = _filePathPointer,
            };
            Pointer = Marshal.AllocCoTaskMem(Marshal.SizeOf<NativeFileInfo>());
            Marshal.StructureToPtr(data, Pointer, false);
        }

        public void Dispose()
        {
            Marshal.FreeCoTaskMem(Pointer);
            Marshal.FreeCoTaskMem(_filePathPointer);
        }
    }

    private sealed class WinTrustData : IDisposable
    {
        public IntPtr Pointer { get; }

        public WinTrustData(IntPtr fileInfo)
        {
            var data = new NativeTrustData
            {
                StructSize = (uint)Marshal.SizeOf<NativeTrustData>(),
                UiChoice = 2,
                RevocationChecks = 0,
                UnionChoice = 1,
                FileInfo = fileInfo,
                StateAction = 0,
                ProviderFlags = 0x00000040,
            };
            Pointer = Marshal.AllocCoTaskMem(Marshal.SizeOf<NativeTrustData>());
            Marshal.StructureToPtr(data, Pointer, false);
        }

        public void Dispose() => Marshal.FreeCoTaskMem(Pointer);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeFileInfo
    {
        public uint StructSize;
        public IntPtr FilePath;
        public IntPtr FileHandle;
        public IntPtr KnownSubject;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeTrustData
    {
        public uint StructSize;
        public IntPtr PolicyCallbackData;
        public IntPtr SipClientData;
        public uint UiChoice;
        public uint RevocationChecks;
        public uint UnionChoice;
        public IntPtr FileInfo;
        public uint StateAction;
        public IntPtr StateData;
        public string? UrlReference;
        public uint ProviderFlags;
        public uint UiContext;
        public IntPtr SignatureSettings;
    }
}
