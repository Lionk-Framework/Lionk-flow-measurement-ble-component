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
    private readonly Queue<(DateTime, int[])> _dataToProcess = new();
    private float _pipeDiameter;
    private float _pipeContentPerMeter;

    private const string DataServiceId = "19B10000-E8F2-537E-4F6C-D104768A1214";
    private const string DataCharacteristicId = "19B10001-E8F2-537E-4F6C-D104768A1214";

    private (DateTime, int[]) _lastValues = (DateTime.MinValue, []);

    public  static string CommonName = "Lionk-Flow";

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
    /// Gets or sets the diameter of the pipe in mm.
    /// </summary>
    public float PipeDiameter
    {
        get { return _pipeDiameter; }
        set
        {
            if (value > 0)
            {
                _pipeDiameter = value;
                var pipeSection = (float)(Math.PI * Math.Pow(_pipeDiameter / 2, 2));
                _pipeContentPerMeter = pipeSection / 1000;
                SetField(ref _pipeDiameter, value);
            }
        }
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
            _bleService?.Subscribe(_deviceAddress, DataServiceId, DataCharacteristicId, this);
        }
    }

    /// <inheritdoc/>
    public void OnDisconnected()
    {
        Console.WriteLine("Disonnected");
    }

    public void OnNotify(string uuid, byte[] data)
    {
        DateTime currentDateTime = DateTime.UtcNow;
        int payloadVersion = (int)data[0];
        int[] datas;
        switch (payloadVersion)
        {
            case 0:
                datas = DecodePayloadV0(data);
                break;
            default:
                Console.WriteLine($"Unknown payload version: {payloadVersion}");
                datas = [];
                break;
        }

        if (_dataToProcess.Count >= QueueSize)
            _dataToProcess.Dequeue();
        _dataToProcess.Enqueue((currentDateTime, datas));
        LastNotifyDateTime = currentDateTime;
        Measure();
    }

    public override void Measure()
    {
        float minSpeed = 0;
        float maxSpeed = 0;
        float averageSpeed = 0;
        foreach (var dataToProcess in _dataToProcess)
        {
            minSpeed = GetSpeed(dataToProcess.Item2.Min());
            maxSpeed = GetSpeed(dataToProcess.Item2.Max());
            averageSpeed = GetSpeed((float)dataToProcess.Item2.Average());
            Measures[0] = new Measure<float>(FlowRateType.SpeedMin.ToString(), dataToProcess.Item1,
                FlowRateType.SpeedMin.GetUnit(), minSpeed);
            Measures[1] = new Measure<float>(FlowRateType.SpeedMax.ToString(), dataToProcess.Item1,
                FlowRateType.SpeedMax.GetUnit(), maxSpeed);
            Measures[2] = new Measure<float>(FlowRateType.SpeedAverage.ToString(), dataToProcess.Item1,
                FlowRateType.SpeedAverage.GetUnit(), averageSpeed);
            Measures[3] = new Measure<float>(FlowRateType.FlowRateMin.ToString(), dataToProcess.Item1,
                FlowRateType.FlowRateMin.GetUnit(), GetFlowRate(minSpeed));
            Measures[4] = new Measure<float>(FlowRateType.FlowRateMax.ToString(), dataToProcess.Item1,
                FlowRateType.FlowRateMax.GetUnit(), GetFlowRate(maxSpeed));
            Measures[5] = new Measure<float>(FlowRateType.FlowRateAverage.ToString(), dataToProcess.Item1,
                FlowRateType.FlowRateAverage.GetUnit(), GetFlowRate(averageSpeed));

            if (_lastValues.Item1 == DateTime.MinValue)
            {
                CurrentValue = 0;
            }
            else
            {
                CurrentValue += Measures[5].Value * (float)(dataToProcess.Item1 - _lastValues.Item1).TotalSeconds;
            }
            _lastValues = dataToProcess;
            base.Measure();
        }
    }

    private float GetSpeed(int value)
    {
        float speed = value / 100f;
        return speed;
    }

    private float GetSpeed(float value)
    {
        float speed = value / 100f;
        return speed;
    }

    private float GetFlowRate(float speed)
    {
        float flowRateLs = _pipeContentPerMeter * speed;

        return flowRateLs;
    }

    private int[] DecodePayloadV0(byte[] data)
    {
        int size = (int)data[1];
        int headerSize = 2;
        int sampleSize = 2;
        int[] values = new int[size];
        for (int i = 0; i < size; i++)
        {
            int msb = data[headerSize + i * sampleSize];
            int lsb = data[headerSize + i * sampleSize + 1];
            values[i] = (msb << 8) | lsb;
        }
        return values;
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
