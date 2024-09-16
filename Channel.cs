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

            public enum ChannelEventCode : byte
            {
                EVENT_RX_SEARCH_TIMEOUT = 0x01, // A receive channel has timed out on searching. The search is terminated, and the channel has been automatically closed.
                EVENT_RX_FAIL = 0x02, // A receive channel missed a message which it was expecting.
                EVENT_TX = 0x03, // A Broadcast message has been transmitted successfully.
                EVENT_TRANSFER_RX_FAILED = 0x04, // A receive transfer has failed.
                EVENT_TRANSFER_TX_COMPLETED = 0x05, // An Acknowledged Data message or a Burst Transfer sequence has been completed successfully.
                EVENT_TRANSFER_TX_FAILED = 0x06, // An Acknowledged Data message, or a Burst Transfer Message has been initiated and the transmission failed to complete successfully.
                EVENT_CHANNEL_CLOSED = 0x07, // The channel has been successfully closed.
                EVENT_RX_FAIL_GO_TO_SEARCH = 0x08, // The channel has dropped to search mode after missing too many messages.
                EVENT_CHANNEL_COLLISION = 0x09, // Two channels have drifted into each other and overlapped in time on the device causing one channel to be blocked.
                EVENT_TRANSFER_TX_START = 0x0A, // Sent after a burst transfer begins, effectively on the next channel period after the burst transfer message has been sent to the device.
                EVENT_TRANSFER_NEXT_DATA_BLOCK = 0x11, // Returned to indicate a data block release on the burst buffer.
                EVENT_SERIAL_QUE_OVERFLOW = 0x34, // This event indicates that the outgoing serial buffer of the USB chip has overflowed.
                EVENT_QUE_OVERFLOW = 0x35, // May be possible when using synchronous serial port, or using all channels on a slow asynchronous connection.
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
            internal event EventHandler<ChannelEventCode> ChannelEvent;

            private readonly Dongle device;
            private readonly byte number;

            private readonly ConcurrentQueue<ChannelMessage> channelMessages;
            private readonly ConcurrentBag<ChannelId> channelIdBag;

            private bool disablePublicEventHandling;

            public Channel(Dongle Device, int Number)
            {
                device = Device;
                number = (byte)Number;
                channelMessages = new ConcurrentQueue<ChannelMessage>();
                channelIdBag = new ConcurrentBag<ChannelId>();
                disablePublicEventHandling = false;
            }

            public void Open()
            {
                device.WriteMessage(new Messages.OpenChannel(number));
                CheckResponse(MessageId.OpenChannel);
            }

            public void Close()
            {
                disablePublicEventHandling = true;
                device.WriteMessage(new Messages.CloseChannel(number));
                CheckResponse(MessageId.CloseChannel);
                if (!WaitForEvent(ChannelMessage.MessageCode.EVENT_CHANNEL_CLOSED)) throw new Exception("No closing event received");
                disablePublicEventHandling = false;
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
                device.WriteMessage(new Messages.Request(number, MessageId.ChannelId));
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
                if(message.Type == ChannelMessage.MessageType.EVENT)
                {
                    Debug.WriteLine("Got Channel event: " + message.Code);
                    if(!disablePublicEventHandling)
                    {
                        //Invoke the handler in a new task otherwise the receiving thread is blocked
                        Task.Run(() => { ChannelEvent?.Invoke(this, (ChannelEventCode)message.Code); });
                        return;
                    }
                }

                channelMessages.Enqueue(message);
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
