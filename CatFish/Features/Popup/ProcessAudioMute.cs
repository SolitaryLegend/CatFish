using System.Runtime.InteropServices;

namespace CatFish.Features.Popup;

internal sealed class ProcessAudioMute : IDisposable
{
    private sealed record OriginalSession(object Control, ISimpleAudioVolume Volume, bool Muted);
    private readonly Dictionary<string, OriginalSession> _original = new();
    private uint _processId;
    public bool IsMuted { get; private set; }
    public int SessionCount => _original.Count;
    internal string LastError { get; private set; } = string.Empty;
    internal bool AllSessionsMuted => _original.Count > 0 && _original.Values.All(session => session.Volume.GetMute(out var muted) >= 0 && muted);

    public bool SetMuted(int processId, bool muted, out string message)
    {
        if (!muted) { Restore(); message = "已恢复目标应用原来的声音状态。"; return true; }
        if (processId <= 0) { message = "请先选择应用窗口。"; return false; }
        if (_processId != processId) Restore();
        _processId = (uint)processId; IsMuted = true;
        return Refresh(out message);
    }

    public bool Refresh(out string message)
    {
        message = string.Empty;
        LastError = string.Empty;
        if (!IsMuted) return true;
        object? enumeratorObject = null; IMMDeviceCollection? devices = null;
        try
        {
            var processIds = Descendants(_processId);
            enumeratorObject = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"))!);
            var enumerator = (IMMDeviceEnumerator)enumeratorObject!;
            Marshal.ThrowExceptionForHR(enumerator.EnumAudioEndpoints(0, 1, out devices));
            Marshal.ThrowExceptionForHR(devices.GetCount(out var count));
            for (uint index = 0; index < count; index++)
            {
                IMMDevice? device = null; object? managerObject = null; IAudioSessionEnumerator? sessions = null;
                try
                {
                    if (devices.Item(index, out device) < 0) continue;
                    var managerId = typeof(IAudioSessionManager2).GUID;
                    if (device.Activate(ref managerId, 23, nint.Zero, out managerObject) < 0) continue;
                    if (((IAudioSessionManager2)managerObject).GetSessionEnumerator(out sessions) < 0 || sessions.GetCount(out var sessionCount) < 0) continue;
                    for (var sessionIndex = 0; sessionIndex < sessionCount; sessionIndex++)
                    {
                        object? controlObject = null; var retained = false;
                        try
                        {
                            if (sessions.GetSession(sessionIndex, out controlObject) < 0 || controlObject is not IAudioSessionControl2 control
                                || control.GetProcessId(out var owner) < 0 || !processIds.Contains(owner)
                                || control.GetSessionInstanceIdentifier(out var id) < 0 || controlObject is not ISimpleAudioVolume volume) continue;
                            if (!_original.ContainsKey(id))
                            {
                                if (volume.GetMute(out var originalMute) < 0 || volume.SetMute(true, nint.Zero) < 0) continue;
                                _original[id] = new(controlObject, volume, originalMute); retained = true;
                            }
                            else volume.SetMute(true, nint.Zero);
                        }
                        finally { if (!retained) Release(controlObject); }
                    }
                }
                finally { Release(sessions); Release(managerObject); Release(device); }
            }
            message = _original.Count > 0 ? "目标应用已静音；再次点击恢复声音。" : "静音已开启，等待目标应用开始播放音频。";
            return true;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or UnauthorizedAccessException)
        { LastError = ex.Message; message = $"暂时无法访问目标音频会话（0x{ex.HResult:X8}），请在平台内静音或稍后重试。"; return false; }
        finally { Release(devices); Release(enumeratorObject); }
    }

    public void Restore()
    {
        foreach (var session in _original.Values)
        {
            try { session.Volume.SetMute(session.Muted, nint.Zero); }
            catch (COMException) { }
            finally { Release(session.Control); }
        }
        _original.Clear(); _processId = 0; IsMuted = false;
    }
    public void Dispose() => Restore();
    private static void Release(object? instance) { if (instance is not null && Marshal.IsComObject(instance)) Marshal.ReleaseComObject(instance); }

    private static HashSet<uint> Descendants(uint root)
    {
        var result = new HashSet<uint> { root }; var parents = new Dictionary<uint, uint>();
        var snapshot = CreateToolhelp32Snapshot(2, 0);
        if (snapshot == new nint(-1)) return result;
        try
        {
            var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>() };
            if (Process32First(snapshot, ref entry))
                do { parents[entry.ProcessId] = entry.ParentProcessId; } while (Process32Next(snapshot, ref entry));
            bool changed;
            do
            {
                changed = false;
                foreach (var pair in parents) if (result.Contains(pair.Value) && result.Add(pair.Key)) changed = true;
            } while (changed);
        }
        finally { CloseHandle(snapshot); }
        return result;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry
    {
        public uint Size, Usage, ProcessId; public nint DefaultHeapId; public uint ModuleId, Threads, ParentProcessId;
        public int BasePriority; public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string FileName;
    }
    [DllImport("kernel32.dll")] private static extern nint CreateToolhelp32Snapshot(uint flags, uint processId);
    [DllImport("kernel32.dll", EntryPoint = "Process32FirstW", CharSet = CharSet.Unicode)] private static extern bool Process32First(nint snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll", EntryPoint = "Process32NextW", CharSet = CharSet.Unicode)] private static extern bool Process32Next(nint snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(nint handle);

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int flow, uint mask, out IMMDeviceCollection devices);
    }
    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IMMDevice device);
    }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid id, uint context, nint parameters, [MarshalAs(UnmanagedType.IUnknown)] out object result);
    }
    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionManager2
    {
        [PreserveSig] int GetAudioSessionControl(nint id, uint flags, out nint control);
        [PreserveSig] int GetSimpleAudioVolume(nint id, uint flags, out nint volume);
        [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator sessions);
    }
    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionEnumerator
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetSession(int index, [MarshalAs(UnmanagedType.IUnknown)] out object control);
    }
    [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionControl2
    {
        [PreserveSig] int GetState(out int state);
        [PreserveSig] int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
        [PreserveSig] int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string name, nint context);
        [PreserveSig] int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);
        [PreserveSig] int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string path, nint context);
        [PreserveSig] int GetGroupingParam(out Guid grouping);
        [PreserveSig] int SetGroupingParam(ref Guid grouping, nint context);
        [PreserveSig] int RegisterAudioSessionNotification(nint notification);
        [PreserveSig] int UnregisterAudioSessionNotification(nint notification);
        [PreserveSig] int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetProcessId(out uint processId);
    }
    [ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISimpleAudioVolume
    {
        [PreserveSig] int SetMasterVolume(float volume, nint context);
        [PreserveSig] int GetMasterVolume(out float volume);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool muted, nint context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool muted);
    }
}
