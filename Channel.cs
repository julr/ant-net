using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Ant
{
    public partial class Dongle
    {
        //TODO: This whole thing is destined to be written as async code. Learn it and rewrite whenever possible
        public class Channel
        {
            public enum ChannelType : byte
            {
                Receive = 0x00,
                Transmit = 0x10,
                SharedReceive = 0x20,
                SharedTransmit = 0x30,
                ReceiveOnly = 0x40,
                TransmitOnly = 0x50
            }

            public class ChannelId
            {
                public ushort DeviceNumber { get; private set; }
                public byte DeviceTypeId { get; private set; }
                public bool PairingRequest { get; private set; }
                public byte TransmissionType { get; private set; }

                internal ChannelId(Message message)
                {
                    if (message.Id != MessageId.ChannelId) throw new ArgumentException("Invalid message type");
                    var payload = message.GetPayload();
                    DeviceNumber = (ushort)(payload[1] | (payload[2] << 8));
                    DeviceTypeId = (byte)(payload[3] & 0x7F);
                    PairingRequest = ((payload[3] & 0x80) == 0x80);
                    TransmissionType = payload[4];
                }
            }

            internal event EventHandler<BroadcastMessage> NewBoradcastMessage;

            private readonly Dongle device;
            private readonly byte number;

            private readonly ConcurrentQueue<ChannelMessage> channelMessages;
            private readonly ConcurrentBag<ChannelId> channelIdBag;

            public Channel(Dongle Device, int Number)
            {
                device = Device;
                number = (byte)Number;
                channelMessages = new ConcurrentQueue<ChannelMessage>();
                channelIdBag = new ConcurrentBag<ChannelId>();
            }

            public void Open()
            {
                device.WriteMessage(new Messages.OpenChannel(number));
                CheckResponse(MessageId.OpenChannel);
            }

            public void Close()
            {
                device.WriteMessage(new Messages.CloseChannel(number));
                CheckResponse(MessageId.CloseChannel);
                if (!WaitForEvent(ChannelMessage.MessageCode.EVENT_CHANNEL_CLOSED)) throw new Exception("No closing event received");
            }

            public void Assign(ChannelType type, byte network)
            {
                device.WriteMessage(new Messages.AssignChannel(number, (byte) type, network));
                CheckResponse(MessageId.AssignChannel);
            }
            public void Unassign()
            {
                device.WriteMessage(new Messages.UnassignChannel(number));
                CheckResponse(MessageId.UnassignChannel);
            }

            public void SetId(ushort DeviceNumber, byte DeviceType, bool DeviceParingRequest = false, byte TransmissionType = 0)
            {
                if (DeviceType > 127) throw new ArgumentException("Device Type needs to be between 0 and 127");
                if (DeviceParingRequest) DeviceType |= 0x80;

                device.WriteMessage(new Messages.ChannelId(number, DeviceNumber, DeviceType, TransmissionType));
                CheckResponse(MessageId.ChannelId);
            }

            public void SetMessagePerios(ushort Period = 8192) // channel messaging period in seconds * 32768
            {
                device.WriteMessage(new Messages.ChannelMessagePeriod(number, Period));
                CheckResponse(MessageId.ChannelPeriod);
            }

            public void SetFrequency(byte ChannelFrequencyNumber = 66) //Channel Frequency = 2400 MHz + Channel RF Frequency Number * 1.0 MHz
            {
                if (ChannelFrequencyNumber > 124) throw new ArgumentException("Channel frequency number must be between 0 and 124");
                device.WriteMessage(new Messages.ChannelRFFrequency(number, ChannelFrequencyNumber));
                CheckResponse(MessageId.ChannelRFFrequency);
            }

            public void SetSearchTimeout(byte Timeout = 10) //Each count in this parameter is equivalent to 2.5 seconds
            {
                device.WriteMessage(new Messages.ChannelSearchTimeout(number, Timeout));
                CheckResponse(MessageId.ChannelSearchTimeout);
            }

            public ChannelId GetChannelId(int timeout = 500)
            {
                device.WriteMessage(new Messages.Request(MessageId.ChannelId));
                ChannelId channelId;
                while(!channelIdBag.TryTake(out channelId))
                {
                    timeout--;
                    if(timeout <= 0) throw new TimeoutException();
                    Thread.Sleep(1);
                }
                return channelId;
            }

            internal void MessageReceived(ChannelMessage message)
            {
                channelMessages.Enqueue(message);
                if(message.Type == ChannelMessage.MessageType.EVENT)
                {
                    Debug.WriteLine("Got Channel event: " + message.Code);
                }
                //TODO: Handle event messages outside of WaitForEvent()
            }

            internal void MessageReceived(BroadcastMessage message)
            {
                //Invoke the handler in a new task otherwise the receiving thread is blocked
                Task.Run(() => { NewBoradcastMessage?.Invoke(this, message); });
            }

            internal void ChannelIdReceived(ChannelId channelId)
            {
                channelIdBag.Clear();
                channelIdBag.Add(channelId);
            }

            private ChannelMessage WaitForResponse(MessageId id, int timeout = 500)
            {
                while (channelMessages.FirstOrDefault(m => m.Id == id) == null)
                {
                    timeout--;
                    if (timeout == 0) return null;
                    Thread.Sleep(1);
                }

                //Search for the element
                while (true) //TODO: Some kind of sanity check
                {
                    if (channelMessages.TryDequeue(out ChannelMessage tmpMessage))
                    {
                        if ((tmpMessage != null) && (tmpMessage.Id == id))
                            return tmpMessage;
                        else
                            channelMessages.Enqueue(tmpMessage);
                    }
                }
            }

            private bool WaitForEvent(ChannelMessage.MessageCode eventCode, int timeout = 500)
            {
                while (channelMessages.FirstOrDefault(m => m.Code == eventCode) == null)
                {
                    timeout--;
                    if (timeout == 0) return false;
                    Thread.Sleep(1);
                }

                //Search for the element
                while (true) //TODO: Some kind of sanity check
                {
                    if (channelMessages.TryDequeue(out ChannelMessage tmpMessage))
                    {
                        if ((tmpMessage != null) && (tmpMessage.Code == eventCode))
                            return true;
                        else
                            channelMessages.Enqueue(tmpMessage);
                    }
                }
            }

            private void CheckResponse(MessageId messageId)
            {
                var response = WaitForResponse(messageId);
                if (response == null) throw new Exception("No response from device");
                if (response.Code != ChannelMessage.MessageCode.RESPONSE_NO_ERROR) throw new Exception("Error reported");
            }
        }
    }
}
