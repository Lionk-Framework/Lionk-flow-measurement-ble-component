using System.Diagnostics;
using Lionk.Ble.Models;
using Lionk.Components.FlowMeter;
using Lionk.Core;
using Lionk.Core.DataModel;
using Newtonsoft.Json;

namespace Lionk.Ble.FlowMeter;

[NamedElement("Ble Flow Meter", "A Ble flow meter")]
public class BleFlowMeter : BaseFlowMeter, IBleCallback
{
    private BleService? _bleService;
    private Guid _bleServiceId;
    private string? _deviceAddress;
    private readonly Queue<(DateTime, int[])> _velocityDataToProcess = new();
    private float _pipeDiameter;
    private float _pipeContentPerMeter;
    private readonly object _lock = new();

    private const string FlowDataSvc = "19B10000-E8F2-537E-4F6C-D104768A1214";
    private const string FlowDataCharacteristic = "19B10001-E8F2-537E-4F6C-D104768A1214";
    private const string VelocityDataSvc = "19B10010-E8F2-537E-4F6C-D104768A1214";
    private const string VelocityDataCharacteristic = "19B10011-E8F2-537E-4F6C-D104768A1214";
    private const string ConsumptionDataSvc = "19B10020-E8F2-537E-4F6C-D104768A1214";
    private const string ConsumptionDataCharacteristic = "19B10021-E8F2-537E-4F6C-D104768A1214";
    private const string PipeDiameterSvc = "19B10030-E8F2-537E-4F6C-D104768A1214";
    private const string PipeDiameterCharacteristic = "19B10031-E8F2-537E-4F6C-D104768A1214";
    private const string VersionSvc = "19B10040-E8F2-537E-4F6C-D104768A1214";
    private const string VersionCharacteristic = "19B10041-E8F2-537E-4F6C-D104768A1214";

    private (DateTime, int[]) _lastValues = (DateTime.MinValue, []);

    public static string CommonName = "Lionk-Flow";

    /// <summary>
    ///Gets or sets the size of the queue for the data to process.
    /// </summary>
    public int QueueSize { get; set; } = 20;

    /// <summary>
    /// Gets or sets the id of the Ble service.
    /// </summary>
    public Guid BleServiceId
    {
        get => _bleServiceId;
        set => SetField(ref _bleServiceId, value);
    }

    /// <summary>
    /// Gets or sets the Ble service.
    /// </summary>
    [JsonIgnore]
    [IdIs("BleServiceId")]
    public BleService? BleService
    {
        get => _bleService;
        set
        {
            _bleService = value;
            if (_bleService is not null)
            {
                BleServiceId = _bleService.Id;
                Register();
            }
        }
    }

    /// <summary>
    /// Gets or sets the device address.
    /// </summary>
    public string DeviceAddress
    {
        get => _deviceAddress ?? string.Empty;
        set
        {
            if (!string.IsNullOrEmpty(value))
            {
                _deviceAddress = value;
                Register();
                SetField(ref _deviceAddress, value);
            }
        }
    }

    /// <summary>
    /// Gets or sets the last notify date time.
    /// </summary>
    public DateTime LastNotifyDateTime { get; set; }

    /// <inheritdoc/>
    public void OnRegistered()
    {
        if (_deviceAddress is not null)
        {
            _bleService?.Subscribe(_deviceAddress, VelocityDataSvc, VelocityDataCharacteristic, this);
            _bleService?.Subscribe(_deviceAddress, ConsumptionDataSvc, ConsumptionDataCharacteristic, this);
            _bleService?.Subscribe(_deviceAddress, FlowDataSvc, FlowDataCharacteristic, this);
        }
    }

    /// <inheritdoc/>
    public void OnDisconnected()
    {
        Console.WriteLine("Disonnected");
    }

    public void OnNotify(string uuid, byte[] data)
    {
        uuid = uuid.ToUpper();
        switch (uuid)
        {
            case VelocityDataCharacteristic:
                DecodeVelocity(data);
                break;
            case ConsumptionDataCharacteristic:
                DecodeConsumption(data);
                break;
            case FlowDataCharacteristic:
                DecodeFlow(data);
                break;
            default:
                break;
        }
    }
    private void DecodeVelocity(byte[] data)
    {
        DateTime currentDateTime = DateTime.UtcNow;
        int payloadVersion = (int)data[0];
        double[] datas;
        switch (payloadVersion)
        {
            case 0:
                datas = DecodeVelocityV0(data);
                break;
            default:
                Console.WriteLine($"Unknown payload version: {payloadVersion}");
                datas = [];
                break;
        }
        double min = datas.Min();
        double max = datas.Max();
        double avg = datas.Average();

        lock (_lock)
        {
            Measures[0] = new Measure<double>(FlowRateType.SpeedMin.ToString(), currentDateTime, FlowRateType.SpeedMin.GetUnit(), min);
            Measures[1] = new Measure<double>(FlowRateType.SpeedMax.ToString(), currentDateTime, FlowRateType.SpeedMax.GetUnit(), max);
            Measures[2] = new Measure<double>(FlowRateType.SpeedAverage.ToString(), currentDateTime, FlowRateType.SpeedAverage.GetUnit(), avg);
        }

        Measure();
    }
    private void DecodeFlow(byte[] data)
    {
        DateTime currentDateTime = DateTime.UtcNow;
        int payloadVersion = (int)data[0];
        double[] datas;
        switch (payloadVersion)
        {
            case 0:
                datas = DecodeFlowV0(data);
                break;
            default:
                Console.WriteLine($"Unknown payload version: {payloadVersion}");
                datas = [];
                break;
        }
        double min = datas.Min();
        double max = datas.Max();
        double avg = datas.Average();

        lock (_lock)
        {
            Measures[3] = new Measure<double>(FlowRateType.FlowMin.ToString(), currentDateTime, FlowRateType.FlowMin.GetUnit(), min);
            Measures[4] = new Measure<double>(FlowRateType.FlowMax.ToString(), currentDateTime, FlowRateType.FlowMax.GetUnit(), max);
            Measures[5] = new Measure<double>(FlowRateType.FlowAvg.ToString(), currentDateTime, FlowRateType.FlowAvg.GetUnit(), avg);
        }

        Measure();
    }
    private void DecodeConsumption(byte[] data)
    {
        DateTime currentDateTime = DateTime.UtcNow;
        int payloadVersion = (int)data[0];
        double value;
        switch (payloadVersion)
        {
            case 0:
                value = DecodeConsumptionV0(data);
                break;
            default:
                Console.WriteLine($"Unknown payload version: {payloadVersion}");
                value = 0;
                break;
        }

        lock (_lock)
        {
            Measures[6] = new Measure<double>(FlowRateType.Consumption.ToString(), currentDateTime, FlowRateType.Consumption.GetUnit(), value);
        }

        Measure();
    }
    private double[] DecodeVelocityV0(byte[] data)
    {
        int size = (int)data[1];
        int headerSize = 2;
        int sampleSize = 2;
        double[] values = new double[size];
        for (int i = 0; i < size; i++)
        {
            int msb = data[headerSize + i * sampleSize];
            int lsb = data[headerSize + i * sampleSize + 1];
            int value = (msb << 8) | lsb;
            values[i] = value / 100f;
        }

        return values;
    }
    private double[] DecodeFlowV0(byte[] data)
    {
        int size = (int)data[1];
        int headerSize = 2;
        int sampleSize = 2;
        double[] values = new double[size];
        for (int i = 0; i < size; i++)
        {
            int msb = data[headerSize + i * sampleSize];
            int lsb = data[headerSize + i * sampleSize + 1];
            int value = (msb << 8) | lsb;
            values[i] = value / 100f;
        }
        return values;
    }
    private double DecodeConsumptionV0(byte[] data)
    {
        long value = data[1] << 24 | data[2] << 16 | data[3] << 8 | data[4];
        return (double)value;
    }

    private void Register()
    {
        if (_bleService is null || string.IsNullOrEmpty(_deviceAddress))
        {
            return;
        }
        _bleService.RegisterDevice(_deviceAddress, this);
    }
}