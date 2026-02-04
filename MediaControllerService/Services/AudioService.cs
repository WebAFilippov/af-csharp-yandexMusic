using System.Runtime.InteropServices;
using MediaControllerService.Models;

namespace MediaControllerService.Services;

public class AudioService : IDisposable
{
    private const int StepPercent = 3;
    private const uint DEVICE_STATE_ACTIVE = 0x00000001;

    private readonly Dictionary<string, DeviceInfo> _devices = new();
    private readonly object _devicesLock = new object();
    private readonly CancellationTokenSource _cts;
    private Task? _monitorTask;
    
    private IMMDeviceEnumerator? _enumerator;
    private NotificationClient? _notificationClient;
    private string? _defaultDeviceId;

    public event EventHandler<List<AudioDevice>>? OnVolumeChanged;
    public event EventHandler<ErrorData>? OnError;

    public AudioService()
    {
        _cts = new CancellationTokenSource();
        InitializeAudio();
    }

    private void InitializeAudio()
    {
        try
        {
            _enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            
            // Register for device notifications
            _notificationClient = new NotificationClient(this);
            _enumerator.RegisterEndpointNotificationCallback(_notificationClient);
            
            // Initial device enumeration
            RefreshDevicesList();
            
            // Start monitoring volume changes
            _monitorTask = Task.Run(MonitorVolumeChangesAsync);
            
            Console.WriteLine($"[AudioService] Initialized with {_devices.Count} devices");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AudioService] Failed to initialize: {ex.Message}");
            OnError?.Invoke(this, new ErrorData
            {
                Code = "AUDIO_INIT_FAILED",
                Message = $"Failed to initialize audio service: {ex.Message}"
            });
        }
    }

    private void RefreshDevicesList()
    {
        if (_enumerator == null) return;

        try
        {
            lock (_devicesLock)
            {
                var oldDeviceIds = new HashSet<string>(_devices.Keys);
                var newDeviceIds = new HashSet<string>();

                // Enumerate all active audio endpoints
                var result = _enumerator.EnumAudioEndpoints(EDataFlow.eRender, DEVICE_STATE_ACTIVE, out IMMDeviceCollection? deviceCollection);
                if (result != 0 || deviceCollection == null)
                {
                    OnError?.Invoke(this, new ErrorData
                    {
                        Code = "DEVICE_ENUMERATION_FAILED",
                        Message = "Failed to enumerate audio devices"
                    });
                    return;
                }

                deviceCollection.GetCount(out uint deviceCount);
                
                // Get default device
                string? defaultDeviceId = null;
                var defaultResult = _enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out IMMDevice? defaultDevice);
                if (defaultResult == 0 && defaultDevice != null)
                {
                    defaultDeviceId = GetDeviceId(defaultDevice);
                }
                _defaultDeviceId = defaultDeviceId;

                // Process all devices
                for (uint i = 0; i < deviceCount; i++)
                {
                    deviceCollection.Item(i, out IMMDevice? device);
                    if (device == null) continue;

                    var deviceId = GetDeviceId(device);
                    if (string.IsNullOrEmpty(deviceId)) continue;

                    newDeviceIds.Add(deviceId);
                    var deviceName = GetDeviceName(device);
                    var (volume, isMuted) = GetDeviceVolumeInfo(device);
                    var isDefault = deviceId == defaultDeviceId;

                    // Update or add device
                    _devices[deviceId] = new DeviceInfo
                    {
                        Id = deviceId,
                        Name = deviceName,
                        Device = device,
                        Volume = volume,
                        IsMuted = isMuted,
                        IsDefault = isDefault,
                        LastVolume = volume,
                        LastMute = isMuted
                    };
                }

                // Remove devices that no longer exist
                foreach (var deviceId in oldDeviceIds)
                {
                    if (!newDeviceIds.Contains(deviceId))
                    {
                        if (_devices.TryGetValue(deviceId, out var deviceInfo) && deviceInfo.Device is IDisposable disposable)
                        {
                            disposable.Dispose();
                        }
                        _devices.Remove(deviceId);
                    }
                }
            }

            // Notify about device list changes
            NotifyVolumeChanged();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AudioService] Error refreshing device list: {ex.Message}");
            OnError?.Invoke(this, new ErrorData
            {
                Code = "DEVICE_REFRESH_FAILED",
                Message = $"Failed to refresh device list: {ex.Message}"
            });
        }
    }

    private string? GetDeviceId(IMMDevice device)
    {
        try
        {
            device.GetId(out IntPtr idPtr);
            if (idPtr == IntPtr.Zero) return null;
            var id = Marshal.PtrToStringUni(idPtr);
            Marshal.FreeCoTaskMem(idPtr);
            return id;
        }
        catch
        {
            return null;
        }
    }

    private string GetDeviceName(IMMDevice device)
    {
        try
        {
            var storeResult = device.OpenPropertyStore(STGM.STGM_READ, out IPropertyStore? propertyStore);
            if (storeResult != 0 || propertyStore == null) return "Unknown Device";

            var propKey = new PROPERTYKEY
            {
                fmtid = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"),
                pid = 14 // PKEY_Device_FriendlyName
            };

            var valueResult = propertyStore.GetValue(ref propKey, out PROPVARIANT propVariant);
            if (valueResult != 0) return "Unknown Device";

            var name = Marshal.PtrToStringUni(propVariant.pwszVal) ?? "Unknown Device";
            PropVariantClear(ref propVariant);
            
            return name;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AudioService] Error getting device name: {ex.Message}");
            return "Unknown Device";
        }
    }

    private (float Volume, bool IsMuted) GetDeviceVolumeInfo(IMMDevice device)
    {
        try
        {
            var activateResult = device.Activate(IID_IAudioEndpointVolume, 0, IntPtr.Zero, out object? interfaceObject);
            if (activateResult != 0 || interfaceObject == null) return (0, false);

            var volumeInterface = (IAudioEndpointVolume)interfaceObject;
            volumeInterface.GetMasterVolumeLevelScalar(out float volume);
            volumeInterface.GetMute(out bool isMuted);

            return (volume * 100, isMuted);
        }
        catch
        {
            return (0, false);
        }
    }

    private void NotifyVolumeChanged()
    {
        lock (_devicesLock)
        {
            var devices = _devices.Values.Select(d => new AudioDevice
            {
                Id = d.Id,
                Name = d.Name,
                IsDefault = d.IsDefault,
                IsMuted = d.IsMuted,
                Volume = (int)d.Volume
            }).ToList();

            OnVolumeChanged?.Invoke(this, devices);
        }
    }

    private async Task MonitorVolumeChangesAsync()
    {
        while (!_cts.Token.IsCancellationRequested)
        {
            try
            {
                bool hasChanges = false;

                lock (_devicesLock)
                {
                    foreach (var device in _devices.Values)
                    {
                        var (currentVolume, currentMute) = GetDeviceVolumeInfo(device.Device);
                        
                        if (Math.Abs(currentVolume - device.LastVolume) > 0.5 || currentMute != device.LastMute)
                        {
                            device.Volume = currentVolume;
                            device.IsMuted = currentMute;
                            device.LastVolume = currentVolume;
                            device.LastMute = currentMute;
                            hasChanges = true;
                        }
                    }
                }

                if (hasChanges)
                {
                    NotifyVolumeChanged();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AudioService] Error monitoring volume: {ex.Message}");
            }

            await Task.Delay(200, _cts.Token);
        }
    }

    public List<AudioDevice> GetAllDevices()
    {
        lock (_devicesLock)
        {
            return _devices.Values.Select(d => new AudioDevice
            {
                Id = d.Id,
                Name = d.Name,
                IsDefault = d.IsDefault,
                IsMuted = d.IsMuted,
                Volume = (int)d.Volume
            }).ToList();
        }
    }

    public void VolumeUp(int stepPercent = StepPercent, string? deviceId = null)
    {
        ExecuteOnDevice(deviceId, (volume) =>
        {
            var (currentVolume, _) = GetDeviceVolumeInfo(volume.Device);
            float newVolume = Math.Min(currentVolume + stepPercent, 100) / 100.0f;
            volume.SetVolume(newVolume);
            Console.WriteLine($"[AudioService] Volume up on '{volume.Name}': {currentVolume:F0}% → {newVolume * 100:F0}%");
        }, "VOLUME_UP_FAILED");
    }

    public void VolumeDown(int stepPercent = StepPercent, string? deviceId = null)
    {
        ExecuteOnDevice(deviceId, (volume) =>
        {
            var (currentVolume, _) = GetDeviceVolumeInfo(volume.Device);
            float newVolume = Math.Max(currentVolume - stepPercent, 0) / 100.0f;
            volume.SetVolume(newVolume);
            Console.WriteLine($"[AudioService] Volume down on '{volume.Name}': {currentVolume:F0}% → {newVolume * 100:F0}%");
        }, "VOLUME_DOWN_FAILED");
    }

    public void ToggleMute(string? deviceId = null)
    {
        ExecuteOnDevice(deviceId, (volume) =>
        {
            var (_, currentMute) = GetDeviceVolumeInfo(volume.Device);
            volume.SetMute(!currentMute);
            Console.WriteLine($"[AudioService] Mute toggled on '{volume.Name}': {currentMute} → {!currentMute}");
        }, "TOGGLE_MUTE_FAILED");
    }

    public void SetMute(bool mute, string? deviceId = null)
    {
        ExecuteOnDevice(deviceId, (volume) =>
        {
            volume.SetMute(mute);
            Console.WriteLine($"[AudioService] Mute set on '{volume.Name}': {mute}");
        }, "SET_MUTE_FAILED");
    }

    public void SetVolume(float percent, string? deviceId = null)
    {
        ExecuteOnDevice(deviceId, (volume) =>
        {
            float newVolume = Math.Clamp(percent, 0, 100) / 100.0f;
            volume.SetVolume(newVolume);
            Console.WriteLine($"[AudioService] Volume set on '{volume.Name}': {percent:F0}%");
        }, "SET_VOLUME_FAILED");
    }

    private void ExecuteOnDevice(string? deviceId, Action<DeviceInfo> action, string errorCode)
    {
        lock (_devicesLock)
        {
            var targetDeviceId = deviceId ?? _defaultDeviceId;
            
            if (string.IsNullOrEmpty(targetDeviceId) || !_devices.TryGetValue(targetDeviceId, out var device))
            {
                OnError?.Invoke(this, new ErrorData
                {
                    Code = "DEVICE_NOT_FOUND",
                    Message = $"Device '{targetDeviceId ?? "default"}' not found",
                    Details = new { RequestedDeviceId = deviceId, AvailableDevices = _devices.Keys.ToList() }
                });
                return;
            }

            try
            {
                action(device);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AudioService] Error executing action: {ex.Message}");
                OnError?.Invoke(this, new ErrorData
                {
                    Code = errorCode,
                    Message = $"Failed to execute action on device '{device.Name}': {ex.Message}",
                    Details = new { DeviceId = device.Id, DeviceName = device.Name }
                });
            }
        }
    }

    internal void OnDeviceStateChanged(string deviceId, uint newState)
    {
        Console.WriteLine($"[AudioService] Device state changed: {deviceId} → {newState}");
        RefreshDevicesList();
    }

    internal void OnDefaultDeviceChanged(int flow, int role, string defaultDeviceId)
    {
        if (flow == (int)EDataFlow.eRender)
        {
            Console.WriteLine($"[AudioService] Default device changed to: {defaultDeviceId}");
            RefreshDevicesList();
        }
    }

    internal void OnDeviceAdded(string deviceId)
    {
        Console.WriteLine($"[AudioService] Device added: {deviceId}");
        RefreshDevicesList();
    }

    internal void OnDeviceRemoved(string deviceId)
    {
        Console.WriteLine($"[AudioService] Device removed: {deviceId}");
        RefreshDevicesList();
    }

    public void Dispose()
    {
        _cts.Cancel();
        _monitorTask?.Wait(TimeSpan.FromSeconds(1));
        _cts.Dispose();

        if (_enumerator != null && _notificationClient != null)
        {
            try
            {
                _enumerator.UnregisterEndpointNotificationCallback(_notificationClient);
            }
            catch { }
        }

        lock (_devicesLock)
        {
            foreach (var device in _devices.Values)
            {
                if (device.Device is IDisposable disposable)
                {
                    try
                    {
                        disposable.Dispose();
                    }
                    catch { }
                }
            }
            _devices.Clear();
        }

        _notificationClient = null;
        _enumerator = null;
    }

    private class DeviceInfo
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public IMMDevice Device { get; set; } = null!;
        public float Volume { get; set; }
        public bool IsMuted { get; set; }
        public bool IsDefault { get; set; }
        public float LastVolume { get; set; } = -1;
        public bool LastMute { get; set; }

        public void SetVolume(float volume)
        {
            var result = Device.Activate(IID_IAudioEndpointVolume, 0, IntPtr.Zero, out object? interfaceObject);
            if (result != 0 || interfaceObject == null) return;

            var volumeInterface = (IAudioEndpointVolume)interfaceObject;
            volumeInterface.SetMasterVolumeLevelScalar(volume, Guid.Empty);
        }

        public void SetMute(bool mute)
        {
            var result = Device.Activate(IID_IAudioEndpointVolume, 0, IntPtr.Zero, out object? interfaceObject);
            if (result != 0 || interfaceObject == null) return;

            var volumeInterface = (IAudioEndpointVolume)interfaceObject;
            volumeInterface.SetMute(mute, Guid.Empty);
        }
    }

    private class NotificationClient : IMMNotificationClient
    {
        private readonly AudioService _service;

        public NotificationClient(AudioService service)
        {
            _service = service;
        }

        public int OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string pwstrDeviceId, uint dwNewState)
        {
            _service.OnDeviceStateChanged(pwstrDeviceId, dwNewState);
            return 0; // S_OK
        }

        public int OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string pwstrDeviceId)
        {
            _service.OnDeviceAdded(pwstrDeviceId);
            return 0; // S_OK
        }

        public int OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string pwstrDeviceId)
        {
            _service.OnDeviceRemoved(pwstrDeviceId);
            return 0; // S_OK
        }

        public int OnDefaultDeviceChanged(EDataFlow flow, ERole role, [MarshalAs(UnmanagedType.LPWStr)] string pwstrDefaultDeviceId)
        {
            _service.OnDefaultDeviceChanged((int)flow, (int)role, pwstrDefaultDeviceId);
            return 0; // S_OK
        }

        public int OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string pwstrDeviceId, PROPERTYKEY key)
        {
            // Handle property changes if needed
            return 0; // S_OK
        }
    }

    private enum DeviceState
    {
        Active = 0x00000001,
        Disabled = 0x00000002,
        NotPresent = 0x00000004,
        Unplugged = 0x00000008,
        All = 0x0000000F
    }

    // These enums need to match the COM interface definitions
    internal enum EDataFlow
    {
        eRender,
        eCapture,
        eAll
    }

    internal enum ERole
    {
        eConsole,
        eMultimedia,
        eCommunications
    }

    // COM Interfaces
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumerator { }

    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig]
        int EnumAudioEndpoints(EDataFlow dataFlow, uint dwStateMask, out IMMDeviceCollection ppDevices);

        [PreserveSig]
        int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice ppEndpoint);

        [PreserveSig]
        int RegisterEndpointNotificationCallback(IMMNotificationClient pClient);

        [PreserveSig]
        int UnregisterEndpointNotificationCallback(IMMNotificationClient pClient);
    }

    [Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig]
        int Activate(Guid iid, uint dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);

        [PreserveSig]
        int OpenPropertyStore(STGM stgmAccess, out IPropertyStore ppProperties);

        [PreserveSig]
        int GetId(out IntPtr ppstrId);
    }

    [Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        [PreserveSig]
        int GetCount(out uint pcDevices);

        [PreserveSig]
        int Item(uint nDevice, out IMMDevice ppDevice);
    }

    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig]
        int RegisterControlChangeNotify(IAudioEndpointVolumeCallback pNotify);
        [PreserveSig]
        int UnregisterControlChangeNotify(IAudioEndpointVolumeCallback pNotify);
        [PreserveSig]
        int GetChannelCount(out uint pnChannelCount);
        [PreserveSig]
        int SetMasterVolumeLevel(float fLevelDB, Guid pguidEventContext);
        [PreserveSig]
        int SetMasterVolumeLevelScalar(float fLevel, Guid pguidEventContext);
        [PreserveSig]
        int GetMasterVolumeLevel(out float pfLevelDB);
        [PreserveSig]
        int GetMasterVolumeLevelScalar(out float pfLevel);
        [PreserveSig]
        int SetChannelVolumeLevel(uint nChannel, float fLevelDB, Guid pguidEventContext);
        [PreserveSig]
        int SetChannelVolumeLevelScalar(uint nChannel, float fLevel, Guid pguidEventContext);
        [PreserveSig]
        int GetChannelVolumeLevel(uint nChannel, out float pfLevelDB);
        [PreserveSig]
        int GetChannelVolumeLevelScalar(uint nChannel, out float pfLevel);
        [PreserveSig]
        int SetMute([MarshalAs(UnmanagedType.Bool)] bool bMute, Guid pguidEventContext);
        [PreserveSig]
        int GetMute(out bool pbMute);
        [PreserveSig]
        int GetVolumeStepInfo(out uint pnStep, out uint pnStepCount);
        [PreserveSig]
        int VolumeStepUp(Guid pguidEventContext);
        [PreserveSig]
        int VolumeStepDown(Guid pguidEventContext);
        [PreserveSig]
        int QueryHardwareSupport(out uint pdwHardwareSupportMask);
        [PreserveSig]
        int GetVolumeRange(out float pflVolumeMindB, out float pflVolumeMaxdB, out float pflVolumeIncrementdB);
    }

    [Guid("657804FA-D6AD-4496-8F59-63D5F68C7B4F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolumeCallback
    {
        [PreserveSig]
        int OnNotify(IntPtr pNotifyData);
    }

    [Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMNotificationClient
    {
        [PreserveSig]
        int OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string pwstrDeviceId, uint dwNewState);
        [PreserveSig]
        int OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string pwstrDeviceId);
        [PreserveSig]
        int OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string pwstrDeviceId);
        [PreserveSig]
        int OnDefaultDeviceChanged(EDataFlow flow, ERole role, [MarshalAs(UnmanagedType.LPWStr)] string pwstrDefaultDeviceId);
        [PreserveSig]
        int OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string pwstrDeviceId, PROPERTYKEY key);
    }

    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig]
        int GetCount(out uint cProps);
        [PreserveSig]
        int GetAt(uint iProp, out PROPERTYKEY pkey);
        [PreserveSig]
        int GetValue(ref PROPERTYKEY key, out PROPVARIANT pv);
        [PreserveSig]
        int SetValue(ref PROPERTYKEY key, ref PROPVARIANT propvar);
        [PreserveSig]
        int Commit();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROPERTYKEY
    {
        public Guid fmtid;
        public uint pid;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct PROPVARIANT
    {
        [FieldOffset(0)] public ushort vt;
        [FieldOffset(8)] public IntPtr pwszVal;
    }

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PROPVARIANT pvar);

    internal enum STGM
    {
        STGM_READ = 0x00000000,
        STGM_WRITE = 0x00000001,
        STGM_READWRITE = 0x00000002
    }

    private static readonly Guid IID_IAudioEndpointVolume = new Guid("5CDF2C82-841E-4546-9722-0CF74078229A");
}
