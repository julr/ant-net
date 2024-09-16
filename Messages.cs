using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ant
{
    // This class serves as container for pre-built messages or inherited messages classes implementing a specific message
    public static class Messages
    {
#pragma warning disable CA2211 // Non-constant fields should not be visible
        public static Message Reset = new(MessageId.Reset, new byte[] { 0x00 });
#pragma warning restore CA2211 // Non-constant fields should not be visible

        public class Request : Message
        {            
            public Request(byte ChannelId, MessageId RequestId) : base(MessageId.Request, new byte[2] { ChannelId, (byte)RequestId}) { }
        }

        public class SetNetworkKey : Message
        {
            public SetNetworkKey(byte Network, byte[] Key) : 
                base(MessageId.SetNetworkKey, new byte[9] { Network, Key[0], Key[1], Key[2], Key[3], Key[4], Key[5], Key[6], Key[7] } )
            { }
        }

        public class OpenChannel : Message
        {
            public OpenChannel(byte ChannelId) :
                base(MessageId.OpenChannel, new byte[1] { ChannelId })
            { }
        }

        public class CloseChannel : Message
        {
            public CloseChannel(byte ChannelId):
                base(MessageId.CloseChannel, new byte[1] { ChannelId })
            { }
        }

        public class UnassignChannel : Message
        {
            public UnassignChannel(byte ChannelId) :
                base(MessageId.UnassignChannel, new byte[1] { ChannelId })
            { }
        }

        public class AssignChannel : Message
        {
            public AssignChannel(byte ChannelId, byte ChannelType, byte Network) :
                base(MessageId.AssignChannel, new byte[3] { ChannelId, ChannelType, Network })
            { }
        }

        public class ChannelId : Message
        {
            public ChannelId(byte ChannelId, ushort DeviceNumber, byte DeviceType, byte TransmissionType) :
                base(MessageId.ChannelId, new byte[5] { ChannelId, (byte)DeviceNumber, (byte)(DeviceNumber >> 8), DeviceType, TransmissionType })
            { }
        }

        public class ChannelMessagePeriod : Message
        {
            public ChannelMessagePeriod(byte ChannelId, ushort MessagePeriod) :
                base(MessageId.ChannelPeriod, new byte[3] { ChannelId, (byte)MessagePeriod, (byte)(MessagePeriod >> 8)})
            { }
        }

        public class ChannelRFFrequency : Message
        {
            public ChannelRFFrequency(byte ChannelId, byte ChannelFrequency) :
                base(MessageId.ChannelRFFrequency, new byte[2] { ChannelId, ChannelFrequency })
            { }
        }

        public class ChannelSearchTimeout : Message
        {
            public ChannelSearchTimeout(byte ChannelId, byte SearchTimeout) :
                base(MessageId.ChannelSearchTimeout, new byte[2] { ChannelId, SearchTimeout })
            { }
        }
    }
}
