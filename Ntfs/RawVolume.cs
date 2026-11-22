using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace MftParser.Ntfs;

sealed class RawVolume : IDisposable {
    const uint GenericRead = 0x80000000;
    const uint ShareAll = 0x7;
    const uint OpenExisting = 3;

    readonly SafeFileHandle _handle;
    readonly FileStream _stream;

    public RawVolume(char letter) {
        _handle = CreateFile($@"\\.\{letter}:", GenericRead, ShareAll, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        if (_handle.IsInvalid) {
            var error = Marshal.GetLastWin32Error();
            _handle.Dispose();
            throw new Win32Exception(error);
        }
        _stream = new FileStream(_handle, FileAccess.Read, 1, false);
    }

    public int Alignment { get; set; } = 512;

    public void ReadExact(long offset, byte[] buffer, int count) {
        _stream.Position = offset;
        var done = 0;
        while (done < count) {
            var read = _stream.Read(buffer, done, count - done);
            if (read <= 0) throw new EndOfStreamException();
            done += read;
        }
    }

    public byte[] Read(long offset, int count) {
        var start = offset / Alignment * Alignment;
        var end = (offset + count + Alignment - 1) / Alignment * Alignment;
        var temp = new byte[end - start];
        ReadExact(start, temp, temp.Length);
        if (start == offset && temp.Length == count) return temp;

        var result = new byte[count];
        Buffer.BlockCopy(temp, (int)(offset - start), result, 0, count);
        return result;
    }

    public void Dispose() {
        _stream.Dispose();
        _handle.Dispose();
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
}
