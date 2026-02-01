using System.Runtime.InteropServices;

namespace MediaControllerService.Services;

public class AudioService : IDisposable
{
    private const int StepPercent = 3; // Default step 3%
    
    private IAudioEndpointVolume? _endpointVolume;
    private readonly object _lock = new object();
    private readonly CancellationTokenSource _cts;
    private Task? _monitorTask;

    public event EventHandler<(float Volume, bool IsMuted)>? OnVolumeChanged;

    public AudioService()
    {
        _cts = new CancellationTokenSource();
        InitializeAudio();
    }

    private void InitializeAudio()
    {
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            var result = enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out IMMDevice device);
            if (result != 0) throw new COMException("Failed to get default audio endpoint", result);
            
            var activateResult = device.Activate(IID_IAudioEndpointVolume, 0, IntPtr.Zero, out object interfaceObject);
            if (activateResult != 0) throw new COMException("Failed to activate audio endpoint volume", activateResult);
            
            _endpointVolume = (IAudioEndpointVolume)interfaceObject;
            
            // Start monitoring volume changes
            _monitorTask = Task.Run(MonitorVolumeChangesAsync);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AudioService] Failed to initialize: {ex.Message}");
        }
    }

    private async Task MonitorVolumeChangesAsync()
    {
        float lastVolume = -1;
        bool lastMute = false;

        while (!_cts.Token.IsCancellationRequested)
        {
            try
            {
                var (currentVolume, currentMute) = GetVolumeInfo();
                
                if (Math.Abs(currentVolume - lastVolume) > 0.001 || currentMute != lastMute)
                {
                    lastVolume = currentVolume;
                    lastMute = currentMute;
                    OnVolumeChanged?.Invoke(this, (currentVolume, currentMute));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AudioService] Error monitoring volume: {ex.Message}");
            }

            await Task.Delay(200, _cts.Token); // Check every 200ms
        }
    }

    public (float Volume, bool IsMuted) GetVolumeInfo()
    {
        lock (_lock)
        {
            if (_endpointVolume == null)
                return (0, false);

            _endpointVolume.GetMasterVolumeLevelScalar(out float volume);
            _endpointVolume.GetMute(out bool isMuted);
            
            return (volume * 100, isMuted);
        }
    }

    public void VolumeUp(int stepPercent = StepPercent)
    {
        lock (_lock)
        {
            if (_endpointVolume == null) return;

            var (currentVolume, _) = GetVolumeInfo();
            float newVolume = Math.Min(currentVolume + stepPercent, 100) / 100.0f;
            
            _endpointVolume.SetMasterVolumeLevelScalar(newVolume, Guid.Empty);
            Console.WriteLine($"[AudioService] Volume up: {currentVolume:F0}% → {newVolume * 100:F0}%");
        }
    }

    public void VolumeDown(int stepPercent = StepPercent)
    {
        lock (_lock)
        {
            if (_endpointVolume == null) return;

            var (currentVolume, _) = GetVolumeInfo();
            float newVolume = Math.Max(currentVolume - stepPercent, 0) / 100.0f;
            
            _endpointVolume.SetMasterVolumeLevelScalar(newVolume, Guid.Empty);
            Console.WriteLine($"[AudioService] Volume down: {currentVolume:F0}% → {newVolume * 100:F0}%");
        }
    }

    public void ToggleMute()
    {
        lock (_lock)
        {
            if (_endpointVolume == null) return;

            _endpointVolume.GetMute(out bool currentMute);
            _endpointVolume.SetMute(!currentMute, Guid.Empty);
            Console.WriteLine($"[AudioService] Mute toggled: {currentMute} → {!currentMute}");
        }
    }

    public void SetMute(bool mute)
    {
        lock (_lock)
        {
            if (_endpointVolume == null) return;
            _endpointVolume.SetMute(mute, Guid.Empty);
        }
    }

    public void SetVolume(float percent)
    {
        lock (_lock)
        {
            if (_endpointVolume == null) return;
            float volume = Math.Clamp(percent, 0, 100) / 100.0f;
            _endpointVolume.SetMasterVolumeLevelScalar(volume, Guid.Empty);
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _monitorTask?.Wait(TimeSpan.FromSeconds(1));
        _cts.Dispose();
        
        if (_endpointVolume is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    // COM Interfaces for Core Audio API
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumerator { }

    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig]
        int EnumAudioEndpoints(EDataFlow dataFlow, uint dwStateMask, out IMMDeviceCollection ppDevices);

        [PreserveSig]
        int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice ppEndpoint);
    }

    [Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig]
        int Activate(Guid iid, uint dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
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

    private enum EDataFlow
    {
        eRender,
        eCapture,
        eAll
    }

    private enum ERole
    {
        eConsole,
        eMultimedia,
        eCommunications
    }

    private static readonly Guid IID_IAudioEndpointVolume = new Guid("5CDF2C82-841E-4546-9722-0CF74078229A");
}
