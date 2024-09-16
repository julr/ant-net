using LibUsbDotNet;
using LibUsbDotNet.Main;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace Ant
{
    public partial class Dongle
    {
        public Channel[] Channels { get; private set; }
        public int MaximumNetworks { get; private set; } = 0;


        private UsbDevice usbDevice;
        private readonly UsbEndpointReader reader;
        private readonly UsbEndpointWriter writer;

        private struct VidPidCombo
        {
            public readonly int VID;
            public readonly int PID;
            public VidPidCombo(int vid, int pid)
            {
                VID = vid; 
                PID = pid;
            }
        }

       private static readonly VidPidCombo[] supportedDeivces = new VidPidCombo[]
       { 
            new VidPidCombo(0x0FCF, 0x1009), // GARMIN ANT+ USB Dongle
            new VidPidCombo(0x0FCF, 0x1008)  // Dynastream ANTUSB2 
       };

        private bool running = false;

        public Dongle()
        {
            UsbRegistry entry = null;

            foreach(var supportedDevice in supportedDeivces)
            {
                //For some reason the UsbDeviceFinder does not find the device -> search the registry by hand
                entry = UsbDevice.AllDevices.FirstOrDefault(d => d.Pid == supportedDevice.PID && d.Vid == supportedDevice.VID && d.Device != null);
                if (entry != null) break;
            }

            if (entry == null)  throw new Exception("No dongle found.");
            if (!entry.Open(out usbDevice)) throw new Exception("Unable to open dongle device");

            // If this is a "whole" usb device (libusb-win32, linux libusb)
            // it will have an IUsbDevice interface. If not (WinUSB) the 
            // variable will be null indicating this is an interface of a 
            // device.
            IUsbDevice wholeUsbDevice = usbDevice as IUsbDevice;
            if (wholeUsbDevice is not null)
            {
                // This is a "whole" USB device. Before it can be used, 
                // the desired configuration and interface must be selected.

                // Select config #1
                wholeUsbDevice.SetConfiguration(1);

                // Claim interface #0.
                wholeUsbDevice.ClaimInterface(0);
            }

            // open read endpoint 1.
            reader = usbDevice.OpenEndpointReader(ReadEndpointID.Ep01);

            // open write endpoint 1.
            writer = usbDevice.OpenEndpointWriter(WriteEndpointID.Ep01);
        }

        ~Dongle()
        {
            running = false;
            if (usbDevice != null)
            {
                if (usbDevice.IsOpen)
                {
                    // If this is a "whole" usb device (libusb-win32, linux libusb-1.0)
                    // it exposes an IUsbDevice interface. If not (WinUSB) the 
                    // 'wholeUsbDevice' variable will be null indicating this is 
                    // an interface of a device; it does not require or support 
                    // configuration and interface selection.
                    IUsbDevice wholeUsbDevice = usbDevice as IUsbDevice;
                    if (wholeUsbDevice is not null)
                    {
                        // Release interface #0.
                        wholeUsbDevice.ReleaseInterface(0);
                    }

                    usbDevice.Close();
                }
                usbDevice = null;
            }
        }

        //TODO: Make the network configurable
        public void Initialize()
        {
            //Clear the devices message buffer (i.e. if the device is currently active or has not been closed correctly)
            byte[] readBuffer = new byte[4096];
            int bytesRead;
            do
            {
                reader.Read(readBuffer, 100, out bytesRead);
            }while (bytesRead > 0);
            
            Message readMessage;
            ChannelMessage response;

            // Reset the device and except a Startup message
            WriteMessage(Messages.Reset);
            readMessage = ReadMessage();
            if (readMessage.Id != MessageId.Startup) throw new Exception("Dongle reset failed");

            //Get the dongles capabilities
            WriteMessage(new Messages.Request(0, MessageId.Capabilities));
            var capabilities = new Capabilities(ReadMessage());

            Channels = new Channel[capabilities.MaximumChannels];
            for (int i = 0; i < capabilities.MaximumChannels; i++)
            {
                Channels[i] = new Channel(this, i);
            }
            MaximumNetworks = capabilities.MaximumNetworks;

            //Assign the ANT+ Network Key to Network 1
            WriteMessage(new Messages.SetNetworkKey(1, Constants.AntPlusNetworkKey));
            response = new ChannelMessage(ReadMessage());
            if (response.Code != ChannelMessage.MessageCode.RESPONSE_NO_ERROR) throw new Exception("Unable to set network key");

            running = true;
            Task.Run(ReadThread);
        }

        private void WriteMessage(Message message, int timeout = 100)
        {
            writer.Write(message, timeout, out int bytesWritten);
            if (bytesWritten != message.MessageLength)
                throw new Exception("Unable to write message");
        }

        //Read a single message
        private Message ReadMessage(int timeout = 100)
        {
            byte[] readBuffer = new byte[512];
            reader.Read(readBuffer, timeout, out int bytesRead);
            if (bytesRead == 0)
                throw new TimeoutException();

            else if (bytesRead < 5) // A valid message consists of a 3 byte header, a 1 byte checksum and at least 1 byte payload
                throw new Exception("Message received is too short");

            else
                return new Message(readBuffer.Take(bytesRead).ToArray());
        }

        //Read possibly more than one message
        private Message[] ReadMessages(int timeout = 100)
        {
            var messages = new List<Message>();
            byte[] readBuffer = new byte[512];
            reader.Read(readBuffer, timeout, out int bytesRead);
            if (bytesRead == 0) return Array.Empty<Message>();

            //Sometimes more than one message is enqueued, split it up
            var data = readBuffer.Take(bytesRead);

            while (data.Any())
            {
                int entryLength = data.ElementAt(1) + 4;
                messages.Add(new Message(data.Take(entryLength).ToArray()));
                data = data.Skip(entryLength);
            }
            return messages.ToArray();
        }

        private void ReadThread()
        {
            while (running)
            {
                var messages = ReadMessages(10);
                foreach (var message in messages)
                {
                    //Debug.WriteLine("Got message: " + message.Id);
                    switch (message.Id)
                    {
                        case MessageId.ChannelResponseEvent:
                            var channelMessage = new ChannelMessage(message);
                            Channels[channelMessage.Channel].MessageReceived(channelMessage);
                            break;

                        case MessageId.BroadcastData:
                            var broadcastMessage = new BroadcastMessage(message);
                            Channels[broadcastMessage.Channel].MessageReceived(broadcastMessage);
                            break;

                        case MessageId.ChannelId:
                            var channelId = new Channel.ChannelId(message);
                            Channels[message.GetRawData()[3]].ChannelIdReceived(channelId);
                            break;

                        //TODO: Handle more message types
                        default:
                            throw new Exception("Unhanded message received");
                    }
                }
            }
        }
    }
}
