using System;
using System.Runtime.InteropServices;
using System.Threading;
using NAudio.Wave;

namespace GivreClone.App.Services;

// Sortie waveOut minimale, compatible NativeAOT.
//
// NAudio.WaveOutEvent plante sous AOT (WaveHeaderUnprepared) : son WaveHeader est une classe, et le
// marshaleur AOT en recopie la struct à une adresse différente à chaque appel - or le driver suit la
// préparation PAR ADRESSE, donc waveOutWrite voit un buffer non préparé et le thread de lecture meurt.
//
// Ici WAVEHDR est en mémoire non managée et les byte[] sont épinglés : prepare et write voient la même
// adresse. win-x64 uniquement (offsets 64 bits).
internal sealed class PcmWaveOut : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct WAVEFORMATEX
    {
        public ushort wFormatTag;
        public ushort nChannels;
        public uint nSamplesPerSec;
        public uint nAvgBytesPerSec;
        public ushort nBlockAlign;
        public ushort wBitsPerSample;
        public ushort cbSize;
    }

    // WAVEHDR x64 : lpData@0, dwBufferLength@8, dwBytesRecorded@12, dwUser@16, dwFlags@24, dwLoops@28, lpNext@32, reserved@40 → 48o.
    private const int HDR_SIZE          = 48;
    private const int HDR_OFF_LPDATA    = 0;
    private const int HDR_OFF_BUFLEN    = 8;
    private const int HDR_OFF_FLAGS     = 24;

    private const int    CALLBACK_EVENT = 0x00050000;
    private const int    WAVE_MAPPER    = -1;
    private const uint   WHDR_DONE      = 0x00000001;
    private const ushort WAVE_FORMAT_PCM = 1;

    [DllImport("winmm.dll")] private static extern int waveOutOpen(out IntPtr h, int deviceId, ref WAVEFORMATEX fmt, IntPtr callback, IntPtr instance, int flags);
    [DllImport("winmm.dll")] private static extern int waveOutPrepareHeader(IntPtr h, IntPtr hdr, int size);
    [DllImport("winmm.dll")] private static extern int waveOutUnprepareHeader(IntPtr h, IntPtr hdr, int size);
    [DllImport("winmm.dll")] private static extern int waveOutWrite(IntPtr h, IntPtr hdr, int size);
    [DllImport("winmm.dll")] private static extern int waveOutReset(IntPtr h);
    [DllImport("winmm.dll")] private static extern int waveOutClose(IntPtr h);

    private readonly IWaveProvider _source;   // PCM 16 bits
    private readonly int _bufferBytes;
    private readonly int _bufferCount;

    private IntPtr _hWaveOut;
    private IntPtr[] _headers = Array.Empty<IntPtr>();
    private byte[][] _data = Array.Empty<byte[]>();
    private GCHandle[] _pins = Array.Empty<GCHandle>();
    private AutoResetEvent? _signal;
    private Thread? _thread;
    private volatile bool _running;

    // buffers tournants ≥ 2, latence répartie dessus
    public PcmWaveOut(IWaveProvider source16, int totalLatencyMs = 180, int buffers = 3)
    {
        _source = source16;
        _bufferCount = Math.Max(2, buffers);
        var wf = source16.WaveFormat;
        int perBuffer = wf.AverageBytesPerSecond * (totalLatencyMs / _bufferCount) / 1000;
        perBuffer -= perBuffer % wf.BlockAlign;                 // aligné trame
        _bufferBytes = Math.Max(wf.BlockAlign * 256, perBuffer);
    }

    public void Play()
    {
        var wf = _source.WaveFormat;
        var fmt = new WAVEFORMATEX
        {
            wFormatTag      = WAVE_FORMAT_PCM,
            nChannels       = (ushort)wf.Channels,
            nSamplesPerSec  = (uint)wf.SampleRate,
            wBitsPerSample  = (ushort)wf.BitsPerSample,
            nBlockAlign     = (ushort)wf.BlockAlign,
            nAvgBytesPerSec = (uint)wf.AverageBytesPerSecond,
            cbSize          = 0,
        };

        _signal = new AutoResetEvent(false);
        int mr = waveOutOpen(out _hWaveOut, WAVE_MAPPER, ref fmt,
                             _signal.SafeWaitHandle.DangerousGetHandle(), IntPtr.Zero, CALLBACK_EVENT);
        if (mr != 0) throw new InvalidOperationException($"waveOutOpen failed (MMRESULT={mr})");

        _headers = new IntPtr[_bufferCount];
        _data    = new byte[_bufferCount][];
        _pins    = new GCHandle[_bufferCount];
        for (int i = 0; i < _bufferCount; i++)
        {
            _data[i] = new byte[_bufferBytes];
            _pins[i] = GCHandle.Alloc(_data[i], GCHandleType.Pinned);   // adresse stable
            IntPtr hdr = Marshal.AllocHGlobal(HDR_SIZE);
            // zéro tout le WAVEHDR, puis lpData + dwBufferLength
            Marshal.WriteIntPtr(hdr, HDR_OFF_LPDATA, _pins[i].AddrOfPinnedObject());
            Marshal.WriteInt32 (hdr, HDR_OFF_BUFLEN, _bufferBytes);
            Marshal.WriteInt32 (hdr, 12, 0);
            Marshal.WriteIntPtr(hdr, 16, IntPtr.Zero);
            Marshal.WriteInt32 (hdr, HDR_OFF_FLAGS, 0);
            Marshal.WriteInt32 (hdr, 28, 0);
            Marshal.WriteIntPtr(hdr, 32, IntPtr.Zero);
            Marshal.WriteIntPtr(hdr, 40, IntPtr.Zero);
            waveOutPrepareHeader(_hWaveOut, hdr, HDR_SIZE);
            _headers[i] = hdr;
        }

        _running = true;
        _thread = new Thread(Loop) { IsBackground = true, Name = "PcmWaveOut" };
        _thread.Start();
        Logger.Info($"Audio output started (custom waveOut, {_bufferCount}×{_bufferBytes}B)");
    }

    private void Loop()
    {
        try
        {
            for (int i = 0; i < _bufferCount; i++) FillAndWrite(i);   // amorce
            while (_running)
            {
                _signal!.WaitOne(50);
                if (!_running) break;
                for (int i = 0; i < _bufferCount; i++)
                    if ((((uint)Marshal.ReadInt32(_headers[i], HDR_OFF_FLAGS)) & WHDR_DONE) != 0)
                        FillAndWrite(i);                              // buffer fini → re-remplir
            }
        }
        catch (Exception ex) { Logger.Error($"Audio thread crashed: {ex}"); }
    }

    // remplit le buffer i en place puis le ré-empile
    private void FillAndWrite(int i)
    {
        int read = _source.Read(_data[i], 0, _bufferBytes);
        if (read < _bufferBytes)
            Array.Clear(_data[i], Math.Max(0, read), _bufferBytes - Math.Max(0, read));
        waveOutWrite(_hWaveOut, _headers[i], HDR_SIZE);
    }

    public void Dispose()
    {
        _running = false;
        _signal?.Set();
        try { _thread?.Join(300); } catch { /* ignoré */ }

        if (_hWaveOut != IntPtr.Zero)
        {
            try { waveOutReset(_hWaveOut); } catch { }
            for (int i = 0; i < _headers.Length; i++)
                if (_headers[i] != IntPtr.Zero)
                {
                    try { waveOutUnprepareHeader(_hWaveOut, _headers[i], HDR_SIZE); } catch { }
                    Marshal.FreeHGlobal(_headers[i]);
                }
            try { waveOutClose(_hWaveOut); } catch { }
            _hWaveOut = IntPtr.Zero;
        }
        for (int i = 0; i < _pins.Length; i++)
            if (_pins[i].IsAllocated) _pins[i].Free();
        _signal?.Dispose();
    }
}
