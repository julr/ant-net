using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ant
{
    internal static class Constants
    {
        // Officially Keys are only available by signing up as "ANT+ Adopter" but this can be found very easily on the net
        public static readonly byte[] AntPlusNetworkKey = new byte[8] { 0xB9, 0xA5, 0x21, 0xFB, 0xBD, 0x72, 0xC3, 0x45 };
    }
    
    // Some helper classes providing abstraction for incoming messages
    internal class Capabilities
    {
        public int MaximumChannels { get; private set; }
        public int MaximumNetworks { get; private set; }
        public byte StandardOptions { get; private set; }
        public byte AdvancedOptions { get; private set; }
        public byte AdvancedOptions2 { get; private set; }
        public int MaximumSensRcoreChannels { get; private set; }
        public byte AdvancedOptions3 { get; private set; }
        public byte AdvancedOptions4 { get; private set; }

        public Capabilities(Message message)
        {
            if (message.Id != MessageId.Capabilities)
                throw new ArgumentException("Invalid message type");

            // According to "ANT Message Protocol and Usage, Rev 5.1" the size of the capabilities message is not fixed.
            // However it is not clear from documentation what fields are optional. I assume advanced options 3 and 4
            // since they reside after the "Max SensRcore Channels" field

            byte[] messagePayload = message.GetPayload();
            MaximumChannels = messagePayload[0];
            MaximumNetworks = messagePayload[1];
            StandardOptions = messagePayload[2];
            AdvancedOptions = messagePayload[3];
            AdvancedOptions2 = messagePayload[4];
            MaximumSensRcoreChannels = messagePayload[5];
            if(messagePayload.Length > 6)
                AdvancedOptions3 = messagePayload[6];
            if (messagePayload.Length > 7)
                AdvancedOptions3 = messagePayload[7];
        }
    }

    internal class ChannelMessage
    {
        public enum MessageType { EVENT, RESPONSE };

        public enum MessageCode
        {
            RESPONSE_NO_ERROR = 0x00, // Returned on a successful operation
            EVENT_RX_SEARCH_TIMEOUT = 0x01, // A receive channel has timed out on searching. The search is terminated, and the channel has been automatically closed.
            EVENT_RX_FAIL = 0x02, // A receive channel missed a message which it was expecting.
            EVENT_TX = 0x03, // A Broadcast message has been transmitted successfully.
            EVENT_TRANSFER_RX_FAILED  = 0x04, // A receive transfer has failed.
            EVENT_TRANSFER_TX_COMPLETED  = 0x05, // An Acknowledged Data message or a Burst Transfer sequence has been completed successfully.
            EVENT_TRANSFER_TX_FAILED = 0x06, // An Acknowledged Data message, or a Burst Transfer Message has been initiated and the transmission failed to complete successfully.
            EVENT_CHANNEL_CLOSED = 0x07, // The channel has been successfully closed.
            EVENT_RX_FAIL_GO_TO_SEARCH = 0x08, // The channel has dropped to search mode after missing too many messages.
            EVENT_CHANNEL_COLLISION = 0x09, // Two channels have drifted into each other and overlapped in time on the device causing one channel to be blocked.
            EVENT_TRANSFER_TX_START = 0x0A, // Sent after a burst transfer begins, effectively on the next channel period after the burst transfer message has been sent to the device.
            EVENT_TRANSFER_NEXT_DATA_BLOCK = 0x11, // Returned to indicate a data block release on the burst buffer.
            CHANNEL_IN_WRONG_STATE = 0x15, // Returned on attempt to perform an action on a channel that is not valid for the channel’s state
            CHANNEL_NOT_OPENED = 0x16, // Attempted to transmit data on an unopened channel 
            CHANNEL_ID_NOT_SET = 0x18, // Returned on attempt to open a channel before setting a valid ID
            CLOSE_ALL_CHANNELS = 0x19, // Returned when an OpenRxScanMode() command is sent while other channels are open.
            TRANSFER_IN_PROGRESS = 0x1F, // Returned on an attempt to communicate on a channel with a transmit transfer in progress.
            TRANSFER_SEQUENCE_NUMBER_ERROR = 0x20, // Returned when sequence number is out of order on a Burst Transfer
            TRANSFER_IN_ERROR = 0x21, // Returned when a burst message passes the sequence number check but will not be transmitted due to other reasons.
            MESSAGE_SIZE_EXCEEDS_LIMIT = 0x27, // Returned if a data message is provided that is too large.
            INVALID_MESSAGE = 0x28, // Returned when message has invalid parameters
            INVALID_NETWORK_NUMBER = 0x29, // Returned when an invalid network number is provided.As mentioned earlier, valid network numbers are between 0 and MAX_NET-1.
            INVALID_LIST_ID = 0x30,  //Returned when the provided list ID or size exceeds the limit.
            INVALID_SCAN_TX_CHANNEL = 0x31, // Returned when attempting to transmit on ANT channel 0 in scan mode.
            INVALID_PARAMETER_PROVIDED = 0x33, // Returned when invalid configuration commands are requested
            EVENT_SERIAL_QUE_OVERFLOW = 0x34, // This event indicates that the outgoing serial buffer of the USB chip has overflowed.
            EVENT_QUE_OVERFLOW = 0x35, // May be possible when using synchronous serial port, or using all channels on a slow asynchronous connection.
            ENCRYPT_NEGOTIATION_SUCCESS = 0x38, // When an ANT slave has negotiated successfully with an encrypted ANT master this event is passed to both the master and the slave.
            ENCRYPT_NEGOTIATION_FAIL = 0x39, // When an ANT slave fails negotiation with an encrypted ANT master this event is passed to both the master and the slave.
            NVM_FULL_ERROR = 0x40, // Returned when the NVM for SensRcore mode is full.
            NVM_WRITE_ERROR = 0x41, // Returned when writing to the NVM for SensRcore mode fails.
            USB_STRING_WRITE_FAIL = 0x70, // Returned when configuration of a USB descriptor string fails.
            MESG_SERIAL_ERROR_ID = 0xAE, // This message is generated if the ANT chip receives a USB data packet that is not correctly formatted.
            INVALID = 0xFF // Invalid or unknown message code
        }

        public byte Channel { get; private set; }
        public MessageType Type { get; private set; }

        public MessageId Id { get; private set; } //ID of the message that the response corresponds to. Ignore for events
        public MessageCode Code { get; private set; } //The response/event code
        public ChannelMessage(Message message)
        {
            if (message.Id != MessageId.ChannelResponseEvent) throw new ArgumentException("Invalid message type");
            var payload = message.GetPayload();
            Channel = payload[0];
            Id = (MessageId)payload[1];
            Code = (MessageCode)payload[2];
            Type = (payload[1] == 0x01) ? MessageType.EVENT : MessageType.RESPONSE; 
        }
    }

    internal class BroadcastMessage
    {
        public byte Channel { get; private set; }
        public byte[] Data { get; private set; }
        public byte[] ExtendedData { get; private set; }

        public BroadcastMessage(Message message)
        {
            if (message.Id != MessageId.BroadcastData) throw new ArgumentException("Invalid message type");
            var payload = message.GetPayload();
            Channel = payload[0];
            Data = new byte[8];
            Array.Copy(payload, 1, Data, 0, 8);
            int extendedDataLength = (int)message.PayloadLength - 9;
            if (extendedDataLength > 0)
            {
                ExtendedData = new byte[extendedDataLength];
                Array.Copy(payload, 9, ExtendedData, 0, extendedDataLength);
            }
                
        }
    }
}
