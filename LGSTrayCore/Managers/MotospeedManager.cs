using LGSTrayPrimitives;
using LGSTrayPrimitives.MessageStructs;
using MessagePipe;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace LGSTrayCore.Managers
{
    public class MotospeedManager : IDeviceManager, IHostedService, IDisposable
    {
        #region HidApi Native Interop
        [StructLayout(LayoutKind.Sequential)]
        private unsafe struct HidDeviceInfo
        {
            public byte* Path;
            public ushort VendorId;
            public ushort ProductId;
            public byte* SerialNumber;
            public ushort ReleaseNumber;
            public byte* ManufacturerString;
            public byte* ProductString;
            public ushort UsagePage;
            public ushort Usage;
            public int InterfaceNumber;
            public HidDeviceInfo* Next;
            public int BusType;
        }

        [DllImport("hidapi", EntryPoint = "hid_init")]
        private static extern int HidInit();

        [DllImport("hidapi", EntryPoint = "hid_exit")]
        private static extern int HidExit();

        [DllImport("hidapi", EntryPoint = "hid_enumerate")]
        private static unsafe extern HidDeviceInfo* HidEnumerate(ushort vendor_id, ushort product_id);

        [DllImport("hidapi", EntryPoint = "hid_free_enumeration")]
        private static unsafe extern void HidFreeEnumeration(HidDeviceInfo* devs);

        [DllImport("hidapi", EntryPoint = "hid_open_path")]
        private static unsafe extern IntPtr HidOpenPath(byte* path);

        [DllImport("hidapi", EntryPoint = "hid_close")]
        private static extern void HidClose(IntPtr dev);

        [DllImport("hidapi", EntryPoint = "hid_write")]
        private static extern int HidWrite(IntPtr dev, byte[] data, UIntPtr length);

        [DllImport("hidapi", EntryPoint = "hid_read_timeout")]
        private static extern int HidReadTimeout(IntPtr dev, byte[] data, UIntPtr length, int milliseconds);
        #endregion

        private const ushort MOTOSPEED_VID = 0x0BDA;
        private static readonly ushort[] MOTOSPEED_PIDS = [0xFFE0, 0xFFF1, 0xFFF2];
        private static readonly ushort[] PLUGGED_PIDS = [0xFFF1];

        private const byte BATTERY_REQUEST_REPORT_ID = 0xB5;
        private const byte BATTERY_RESPONSE_REPORT_ID = 0xB4;
        private const int BATTERY_VALUE_OFFSET = 20;

        private readonly IPublisher<IPCMessage> _deviceEventBus;
        private readonly AppSettings _settings;

        private CancellationTokenSource? _cts;
        private Task? _pollTask;
        private bool _disposedValue;
        private readonly string _deviceId = "motospeed-x6";

        static MotospeedManager()
        {
            try
            {
                string baseDir = AppContext.BaseDirectory;
                string dllPath = Path.Combine(baseDir, "hidapi.dll");
                if (!File.Exists(dllPath))
                {
                    string subPath = Path.Combine(baseDir, "libhidapi", "hidapi.dll");
                    if (File.Exists(subPath))
                    {
                        NativeLibrary.Load(subPath);
                    }
                    else
                    {
                        string devPath = Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\..\LGSTrayHID\libhidapi\hidapi.dll"));
                        if (File.Exists(devPath))
                        {
                            NativeLibrary.Load(devPath);
                        }
                    }
                }
            }
            catch { }
        }

        public MotospeedManager(
            IPublisher<IPCMessage> deviceEventBus,
            IOptions<AppSettings> settings)
        {
            _deviceEventBus = deviceEventBus;
            _settings = settings.Value;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            _pollTask = Task.Run(async () =>
            {
                bool initialized = false;

                while (!_cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        var result = ReadBattery();

                        if (result.found && !initialized)
                        {
                            _deviceEventBus.Publish(new InitMessage(
                                _deviceId,
                                result.deviceName ?? "Motospeed X6",
                                true,
                                DeviceType.Mouse
                            ));
                            initialized = true;
                        }

                        if (result.found && result.batteryLevel >= 0)
                        {
                            bool isCharging = result.batteryLevel >= 128;
                            int displayLevel = isCharging
                                ? Math.Clamp(result.batteryLevel - 128, 0, 100)
                                : Math.Clamp(result.batteryLevel, 0, 100);

                            var status = result.isPluggedIn || isCharging
                                ? PowerSupplyStatus.POWER_SUPPLY_STATUS_CHARGING
                                : PowerSupplyStatus.POWER_SUPPLY_STATUS_DISCHARGING;

                            _deviceEventBus.Publish(new UpdateMessage(
                                _deviceId,
                                displayLevel,
                                status,
                                -1,
                                DateTimeOffset.Now
                            ));
                        }
                        else if (result.found && result.isPluggedIn)
                        {
                            _deviceEventBus.Publish(new UpdateMessage(
                                _deviceId,
                                -1,
                                PowerSupplyStatus.POWER_SUPPLY_STATUS_CHARGING,
                                -1,
                                DateTimeOffset.Now
                            ));
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Motospeed poll error: {ex.Message}");
                    }

                    try
                    {
                        int pollSeconds = _settings.Motospeed?.PollPeriod ?? 60;
                        await Task.Delay(pollSeconds * 1000, _cts.Token);
                    }
                    catch (OperationCanceledException) { break; }
                }
            }, _cts.Token);

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _cts?.Cancel();
            return _pollTask ?? Task.CompletedTask;
        }

        public void RediscoverDevices()
        {
            _cts?.Cancel();
            _ = StartAsync(CancellationToken.None);
        }

        private unsafe (bool found, int batteryLevel, bool isPluggedIn, string? deviceName) ReadBattery()
        {
            try
            {
                HidInit();
                HidDeviceInfo* root = HidEnumerate(MOTOSPEED_VID, 0);
                if (root == null)
                {
                    return (false, -1, false, null);
                }

                try
                {
                    for (HidDeviceInfo* cur = root; cur != null; cur = cur->Next)
                    {
                        string? path = cur->Path != null ? Marshal.PtrToStringAnsi((IntPtr)cur->Path) : null;
                        int ifNum = cur->InterfaceNumber;
                        bool isInterface4 = ifNum == 4 || (path != null && path.Contains("mi_04", StringComparison.OrdinalIgnoreCase));

                        if (!isInterface4) continue;

                        string? deviceName = null;
                        if (cur->ProductString != null)
                        {
                            deviceName = Marshal.PtrToStringUni((IntPtr)cur->ProductString)?.TrimEnd('\0');
                        }
                        if (string.IsNullOrWhiteSpace(deviceName))
                        {
                            deviceName = "Motospeed X6";
                        }

                        bool isPluggedIn = PLUGGED_PIDS.Contains(cur->ProductId);
                        if (isPluggedIn)
                        {
                            return (true, -1, true, deviceName);
                        }

                        IntPtr handle = HidOpenPath(cur->Path);
                        if (handle == IntPtr.Zero) continue;

                        try
                        {
                            byte[] req = new byte[21];
                            req[0] = BATTERY_REQUEST_REPORT_ID;
                            req[1] = 0x06;

                            int written = HidWrite(handle, req, (UIntPtr)req.Length);
                            if (written <= 0) continue;

                            byte[] resp = new byte[64];
                            int readLen = HidReadTimeout(handle, resp, (UIntPtr)resp.Length, 1500);

                            if (readLen > BATTERY_VALUE_OFFSET && resp[0] == BATTERY_RESPONSE_REPORT_ID)
                            {
                                int rawBat = resp[BATTERY_VALUE_OFFSET];
                                return (true, rawBat, false, deviceName);
                            }
                        }
                        finally
                        {
                            HidClose(handle);
                        }
                    }
                }
                finally
                {
                    HidFreeEnumeration(root);
                    HidExit();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Motospeed ReadBattery error: {ex.Message}");
            }

            return (false, -1, false, null);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    _cts?.Cancel();
                    _cts?.Dispose();
                }
                _disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
