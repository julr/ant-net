using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Ant.Device
{
    public class HeartRateMonitor
    {
        // Device parameters
        private const byte DeviceType = 0x78;
        private const ushort MessagePeriod = 8070;
        private const byte RFChannelNumber = 57;
        private const Ant.Dongle.Channel.ChannelType ChannelType = Dongle.Channel.ChannelType.Receive;
        private const byte SearchTimeout = 12; //equals to 30 seconds

        // Public properties
        public event EventHandler NewSensorDataReceived;

        public event EventHandler<ushort> SensorFound;

        public enum State
        {
            Off,
            Normal,
            Searching
        }

        public State DeviceState { get; private set; } = State.Off;
        public int HeartRate { get; private set; } = -1;
        public int HeartBeatCount { get; private set; } = -1;
        public double HeartBeatEventTime { get; private set; } = -1.0;
        public double PreviousHeartBeatEventTime { get; private set; } = -1.0;
        public int CumulativeOperatingTime { get; private set; } = -1;
        public int ManufacturerId { get; private set; } = -1;
        public int SerialNumber { get; private set; } = -1;
        public int HardwareVersion { get; private set; } = -1;
        public int SoftwareVersion { get; private set; } = -1;
        public int ModelNumber { get; private set; } = -1;

        public event EventHandler SensorNotFound;


        // private variables
        private readonly Mutex messageMutex;

        private readonly Dongle.Channel channel;

        private int lastEventlastEventCount;

        private bool autoReconnect;

        public HeartRateMonitor(Dongle.Channel channel)
        {
            this.channel = channel ?? throw new ArgumentNullException(nameof(channel));
            channel.NewBoradcastMessage += Channel_OnBoradcastMessage;
            channel.ChannelEvent += Channel_ChannelEvent;
            messageMutex = new Mutex(false);
            lastEventlastEventCount = 0;
            autoReconnect = false;
        }

        private void Channel_ChannelEvent(object sender, Dongle.Channel.ChannelEventCode e)
        {
            switch (e)
            {
                case Dongle.Channel.ChannelEventCode.EVENT_CHANNEL_CLOSED:
                    Debug.WriteLine("HRM sensor channel was closed");
                    if(autoReconnect)
                    {
                        channel.Open();
                    }
                    else
                    {
                        channel.Unassign();
                        DeviceState = State.Off;
                    }
                    break;

                case Dongle.Channel.ChannelEventCode.EVENT_RX_SEARCH_TIMEOUT:
                    Debug.WriteLine("HRM sensor was not found, search timeout");
                    SensorNotFound?.Invoke(this, new EventArgs());
                    break;
            }
        }

        // The Event will be fired as a task, add a mutex lock to prevent data corruption
        private void Channel_OnBoradcastMessage(object sender, BroadcastMessage e)
        {
            messageMutex.WaitOne();
            Debug.WriteLine("Broadcast Message: " + BitConverter.ToString(e.Data));
            if (DeviceState == State.Searching)
            {
                var channelId = channel.GetChannelId();
                this.Stop();
                SensorFound?.Invoke(this, channelId.DeviceNumber);
            }
            else
            {
                //Handle Sensor data
                var page = e.Data[0] & 0x7F;
                //if (page > 4) throw new Exception("Invalid Sensor data received");

                //Skip unknown pages
                if (page > 4) return;

                //Common data between all pages
                HeartBeatEventTime = ((int)e.Data[4] | (int)e.Data[5] << 8) / 1024.0;
                HeartBeatCount = (int)e.Data[6];
                HeartRate = (int)e.Data[7];

                //page specific data
                switch (page)
                {
                    case 1:
                        CumulativeOperatingTime = ((int)e.Data[1] | (int)e.Data[2] << 8 | (int)e.Data[3] << 16) * 2;
                        break;

                    case 2:
                        ManufacturerId = (int)e.Data[1];
                        SerialNumber = (int)e.Data[2] | (int)e.Data[3] << 8;
                        break;

                    case 3:
                        HardwareVersion = (int)e.Data[1];
                        SoftwareVersion = (int)e.Data[2];
                        ModelNumber = (int)e.Data[3];
                        break;

                    case 4:
                        PreviousHeartBeatEventTime = ((int)e.Data[2] | (int)e.Data[3] << 8) / 1024.0;
                        break;

                    default:
                        break;
                }
                //do we actually have new data?
                if(HeartBeatCount != lastEventlastEventCount)
                    NewSensorDataReceived?.Invoke(this, null);
                lastEventlastEventCount = HeartBeatCount;
            }

            messageMutex.ReleaseMutex();
        }

        public void StartSearch(byte network = 1)
        {
            Start(0, false, network);  //Device ID 0 = Searching
            DeviceState = State.Searching;
        }

        public void Start(ushort SensorId, bool AutoReconnect = false, byte network = 1)
        {
            if (DeviceState != State.Off) throw new Exception("Device is currently active");
            autoReconnect = AutoReconnect;
            channel.Assign(ChannelType, network);
            channel.SetId(SensorId, DeviceType);
            channel.SetSearchTimeout(SearchTimeout);
            channel.SetMessagePerios(MessagePeriod);
            channel.SetFrequency(RFChannelNumber);
            channel.Open();
            DeviceState = State.Normal;
        }

        public void Stop()
        {
            if (DeviceState != State.Off)
            {
                DeviceState = State.Off;
                channel.Close();
                channel.Unassign();
            }
        }
    }
}
