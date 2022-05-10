using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Storage.Streams;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace DemoBTApi
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        public MainPage()
        {
            this.InitializeComponent();
        }
        DeviceWatcher watcher = null;
        DeviceInformation TrainDeviceInformation = null;
        BluetoothLEDevice TrainLEDevice = null;
        GattCharacteristic TrainCommandCharacteristic = null; // LE Devices have services which have characteristics.

        void ReportStatusLine(string status)
        {
            // This is a method I created to make it easy to run a command on the UI thread.
            // If you're already on the UI thread, ust runs the command right away.
            UIThread.CallOnUIThread(
                () => { uiLog.Text += status + "\n"; }
                );
        }

        /// <summary>
        /// Called when the List button is clicked. Will emumerate all BT LE devices
        /// and will print them out. The one that starts with "LC" is the train,
        /// so it will be and saved.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnListTrains(object sender, RoutedEventArgs e)
        {
            uiLog.Text = "";

            string[] requestedProperties = {
                "System.Devices.Aep.DeviceAddress",
                "System.Devices.Aep.CanPair",
                "System.Devices.Aep.IsConnected",
                "System.Devices.Aep.IsPresent", // Because I often only want devices that are here right now.
                "System.Devices.Aep.SignalStrength",
                "System.Devices.Aep.Bluetooth.Le.Appearance",
                "System.Devices.Aep.Bluetooth.Le.IsConnectable",
                "System.Devices.GlyphIcon",
                "System.Devices.Icon",
            };
            //var qaqsFilter = BluetoothLEDevice.GetDeviceSelectorFromDeviceName("LC*");
            // nope: var qaqsFilter = BluetoothDevice.GetDeviceSelector();

            var qaqsFilter = "System.Devices.Aep.ProtocolId:=\"{BB7BB05E-5972-42B5-94FC-76EAA7084D49}\"";
            watcher = DeviceInformation.CreateWatcher(
                qaqsFilter,
                requestedProperties,
                DeviceInformationKind.AssociationEndpoint);
            watcher.Added += DeviceWatcher_Added;
            watcher.Updated += DeviceWatcher_Updated;
            watcher.Removed += DeviceWatcher_Removed;
            watcher.Stopped += DeviceWatcher_Stopped;
            watcher.EnumerationCompleted += DeviceWatcher_EnumerationCompleted;
            watcher.Start();

        }

        private void DeviceWatcher_EnumerationCompleted(DeviceWatcher sender, object args)
        {
            ReportStatusLine($"Enumeration Complete");
        }

        private void DeviceWatcher_Stopped(DeviceWatcher sender, object args)
        {
            ReportStatusLine($"Stopped");
        }

        private void DeviceWatcher_Removed(DeviceWatcher sender, DeviceInformationUpdate args)
        {
        }

        private void DeviceWatcher_Updated(DeviceWatcher sender, DeviceInformationUpdate args)
        {
        }

        private void DeviceWatcher_Added(DeviceWatcher sender, DeviceInformation args)
        {
            ReportStatusLine($"Found device {args.Name}");
            if (args.Name.StartsWith("LC"))
            {
                // Must be the train
                ReportStatusLine($"** GOT THE TRAIN {args.Name}");
                TrainDeviceInformation = args;
            }
        }


        private async void OnConnectTrain(object sender, RoutedEventArgs e)
        {
            uiLog.Text = "";

            TrainCommandCharacteristic = null;

            if (TrainDeviceInformation == null)
            {
                uiLog.Text += $"No train was found";
                return;
            }
            if (watcher != null && watcher.Status == DeviceWatcherStatus.Started)
            {
                watcher.Stop();
            }
            TrainLEDevice = await BluetoothLEDevice.FromIdAsync(TrainDeviceInformation.Id);
            if (TrainLEDevice == null)
            {
                uiLog.Text += $"Unable to connect to {TrainDeviceInformation.Name} id={TrainDeviceInformation.Id}";
            }

            // Connect to the service and characteristic
            var trainServiceGuid = Guid.Parse("e20a39f4-73f5-4bc4-a12f-17d1ad07a961");
            var trainCommandCharacteristicGuid = Guid.Parse("08590f7e-db05-467e-8757-72f6faeb13d4");
            var serviceStatus = await TrainLEDevice.GetGattServicesForUuidAsync(trainServiceGuid);
            if (serviceStatus.Status != GattCommunicationStatus.Success)
            {
                ReportStatusLine($"Unable to get service {trainServiceGuid}");
                return;
            }
            if (serviceStatus.Services.Count != 1)
            {
                ReportStatusLine($"Unable to get valid service count ({serviceStatus.Services.Count}) for {trainServiceGuid}");
            }
            var service = serviceStatus.Services[0]; // Got the Train service with the command characteristic.

            // Get the command characteristic
            var characteristicsStatus = await service.GetCharacteristicsForUuidAsync(trainCommandCharacteristicGuid);
            if (characteristicsStatus.Status != GattCommunicationStatus.Success)
            {
                ReportStatusLine($"unable to get characteristic for {trainCommandCharacteristicGuid}");
                return;
            }
            if (characteristicsStatus.Characteristics.Count == 0)
            {
                ReportStatusLine($"unable to get any characteristics for {trainCommandCharacteristicGuid}");
                return;
            }
            else if (characteristicsStatus.Characteristics.Count != 1)
            {
                ReportStatusLine($"unable to get correct characteristics count ({characteristicsStatus.Characteristics.Count}) for {trainCommandCharacteristicGuid}");
                return;
            }
            TrainCommandCharacteristic = characteristicsStatus.Characteristics[0];

            ReportStatusLine("Connected to train");
        }


        private async void OnWhistle(object sender, RoutedEventArgs e)
        {
            var ontask = WriteTrainHorn(true);
            await Task.Delay(1_500); // milliseconds
            var offtask = WriteTrainHorn(false);
        }


        private byte CalculateChecksum(byte command, byte[] param)
        {
            byte retval = (byte)((byte)0xFF - command);
            foreach (var b in param)
            {
                retval = (byte)(retval - b);
            }
            return retval;
        }

        private async Task WriteCommandAsync(GattCharacteristic characteristic, string method, byte[] command, GattWriteOption writeOption)
        {
            GattCommunicationStatus result = GattCommunicationStatus.Unreachable;
            try
            {
                result = await characteristic.WriteValueAsync(command.AsBuffer(), writeOption);
            }
            catch (Exception)
            {
                result = GattCommunicationStatus.Unreachable;
            }
            ReportStatusLine(method);
            if (result != GattCommunicationStatus.Success)
            {
                // NOTE: should add a way to reset
            }
        }

        public async Task WriteTrainCommand(byte Zero, byte Command, byte[] Parameters, byte Checksum)
        {
            if (TrainCommandCharacteristic == null)
            {
                ReportStatusLine("ERROR: train is not connected");
                return;
            }

            var dw = new DataWriter();
            // Bluetooth standard: From v4.2 of the spec, Vol 3, Part G (which covers GATT), page 523: Bleutooth is normally Little Endian
            dw.ByteOrder = ByteOrder.LittleEndian;
            dw.UnicodeEncoding = UnicodeEncoding.Utf8;
            dw.WriteByte(Zero);
            dw.WriteByte(Command);
            dw.WriteBytes(Parameters);
            dw.WriteByte(Checksum);

            var command = dw.DetachBuffer().ToArray();
            const int MAXBYTES = 20;
            for (int i = 0; i < command.Length; i += MAXBYTES)
            {
                // So many calculations and copying just to get a slice
                var maxCount = Math.Min(MAXBYTES, command.Length - i);
                var subcommand = new ArraySegment<byte>(command, i, maxCount).ToArray();

                // Write to console
                var str = "COMMAND: ";
                foreach (var b in subcommand)
                {
                    str += $"{b:X2} ";
                }
                System.Diagnostics.Debug.WriteLine(str);

                await WriteCommandAsync(TrainCommandCharacteristic, "Sent Command to train", subcommand, GattWriteOption.WriteWithResponse);
            }
        }



        /// <summary>
        /// The generated version of this include a "zero" byte which is always zero and a checksum which is calculated with the above method.
        /// </summary>
        /// <param name="cmd"></param>
        /// <param name="param"></param>
        /// <returns></returns>
        public Task WriteTrainCommand(byte cmd, byte[] param = null)
        {
            if (param == null) param = new byte[] { };
            return WriteTrainCommand(0, cmd, param, CalculateChecksum(cmd, param));
        }
        public Task WriteTrainCommand(byte cmd, byte paramByte)
        {
            var param = new byte[] { paramByte };
            return WriteTrainCommand(0, cmd, param, CalculateChecksum(cmd, param));
        }

        public Task WriteTrainHorn(bool turnOn)
        {
            return WriteTrainCommand(0x48, turnOn ? (byte)1 : (byte)0);
        }

        public Task WriteTrainBell(bool turnOn)
        {
            return WriteTrainCommand(0x47, turnOn ? (byte)1 : (byte)0);
        }
    }
}
