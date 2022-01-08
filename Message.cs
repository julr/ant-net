using System;
using System.Linq;

namespace Ant
{
    public enum MessageId : byte
    {
        // ---- Configuration ---- //
        UnassignChannel          = 0x41,
        AssignChannel            = 0x42,
        ChannelId                = 0x51,
        ChannelPeriod            = 0x43,
        ChannelSearchTimeout     = 0x44,
        ChannelRFFrequency       = 0x45,
        SetNetworkKey            = 0x46,
        LowPrioritySearchTimeout = 0x63,

        // ---- Notification ---- //
        Startup = 0x6F,

        // ---- Control ---- //
        Reset                    = 0x4A,
        OpenChannel              = 0x4B,
        CloseChannel             = 0x4C,
        Request                  = 0x4D,

        // ---- Data ---- //
        BroadcastData            = 0x4E,

        // ---- Channel ---- //
        ChannelResponseEvent     = 0x40,

        // ---- Request/Response ---- //
        Capabilities = 0x54,

        // ---- Test Mode ---- //

        // ---- Extended Data ---- //
    }

    public class Message
    {
        public MessageId Id
        {
            get { return (MessageId)rawData[2]; }
        }

        public byte PayloadLength
        {
            get { return rawData[1]; }
        }

        public int MessageLength
        {
            get { return rawData.Length; }
        }

        public bool ChecksumValid
        {
            get { return rawData[^1] == CalculateChecksum(); }
        }

        public byte[] GetRawData() => rawData;

        public byte[] GetPayload() => rawData.Skip(3).SkipLast(1).ToArray();

        public static implicit operator byte[](Message m) => m.GetRawData();


        private const byte SYNC = 0xA4;

        private readonly byte[] rawData;

        //Construct a new message object from ID and payload
        public Message(MessageId Id, byte[] Payload)
        {
            rawData = new byte[Payload.Length + 4]; //Message consist of 3 byte Header + Payload + Checksum
            rawData[0] = SYNC;
            rawData[1] = (byte)Payload.Length;
            rawData[2] = (byte)Id;
            Array.Copy(Payload, 0, rawData, 3, Payload.Length);
            rawData[^1] = CalculateChecksum();
        }

        //Construct a new message object form a raw data buffer
        public Message(byte[] RawData)
        {
            //Check if the message starts with SYNC
            if (RawData[0] != SYNC) throw new ArgumentException("Message does not start with SYNC byte");
            //Check if the message id is known
            if (!Enum.IsDefined(typeof(MessageId), RawData[2])) throw new ArgumentException("Unknown message id");
            //Check if the length is correct
            if (RawData.Length < RawData[1] + 4) throw new ArgumentException("Invalid message length");

            rawData = RawData.ToArray(); //Copy the content
        }

        private byte CalculateChecksum()
        {
            byte checksum = 0;
            for (int i = 0; i < rawData.Length-1; i++)
            {
                checksum ^= rawData[i];
            }

            return checksum;
        }
    }
}
