using System;
using System.ComponentModel;
using System.Linq;
using System.Management;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Bojote.gTenxor
{
    /// <summary>
    /// Logic for SettingsControl.xaml
    /// </summary>
    public partial class SettingsControl : UserControl
    {
        // Needed for internal stuff
        private readonly SemaphoreSlim semaphore = new SemaphoreSlim(1);

        // Some custom variables 
        private ManagementEventWatcher _watcher;
        public bool isReady = false; // Declare the shared variable

        // Static variables
        private static bool watcherStarted = false;
        private static bool prevDeviceStatePermanent; // Declare the shared variable
        private static string prevDevicePermanent; // Declare the shared variable

        public SerialConnection SerialConnection { get; set; }

        public GTenxor Plugin { get; }

        public SettingsControl()
        {
            SimHub.Logging.Current.Info("BEGIN -> SettingsControl1");

            // Initialization
            InitializeComponent();

            SimHub.Logging.Current.Info("END -> SettingsControl1");
        }

        public SettingsControl(GTenxor plugin) : this()
        {
            SimHub.Logging.Current.Info("BEGIN -> SettingsControl2");

            this.Plugin = plugin;
            DataContext = Plugin.Settings;

            // Initialize SerialConnection if none was set
            if (Plugin.SerialConnection != null)
                SerialConnection = Plugin.SerialConnection;

            // Initialize SerialConnection for gTenxor (if none was set)
            if (Plugin.SerialConnection == null && SerialConnection != null)
                Plugin.SerialConnection = SerialConnection;

            // If after the above checks, both are still null, create a new SerialConnection object.
            if (Plugin.SerialConnection == null && SerialConnection == null)
            {
                SerialConnection = new SerialConnection();
                Plugin.SerialConnection = SerialConnection;
                SimHub.Logging.Current.Info("Instantiated new SerialConnection");
            }

            // Load the initial list of serial devices
            SerialConnection.LoadSerialDevices();

            // Bind the devices to the ComboBox
            SerialDevicesComboBox.ItemsSource = SerialConnection.SerialDevices;

            // Bind the device speeds to the ComboBox
            BaudRateComboBox.ItemsSource = SerialConnection.BaudRates;

            // Need to Load some of my events
            Loaded -= SettingsControl_Loaded;
            Loaded += SettingsControl_Loaded;

            // And we also need to be able to unload them
            Unloaded -= SettingsControl_Unloaded;
            Unloaded += SettingsControl_Unloaded;

            // We need to make sure there is only one event always active
            SerialConnection.OnDataReceived -= HandleDataReceived;
            SerialConnection.OnDataReceived += HandleDataReceived;

            // Load Events from SerialConnection class
            SerialConnection.PropertyChanged -= SerialConnection_PropertyChanged;
            SerialConnection.PropertyChanged += SerialConnection_PropertyChanged;

            // Load Events from for combo box changes
            SerialDevicesComboBox.SelectionChanged -= ComboBox_SelectionChanged;
            SerialDevicesComboBox.SelectionChanged += ComboBox_SelectionChanged;

            // Load events from SettingsControl class
            ConnectCheckBox.Checked -= Connect_Checked;
            ConnectCheckBox.Unchecked -= Connect_Unchecked;
            BaudRateComboBox.SelectionChanged -= ComboBox_SelectionChanged;
            ConnectCheckBox.Checked += Connect_Checked;
            ConnectCheckBox.Unchecked += Connect_Unchecked;
            BaudRateComboBox.SelectionChanged += ComboBox_SelectionChanged;

            // Connect automatically if setting is active
            Task.Run(() => TryConnect(null,0));

            // Need to figure out a more efficient way to do this and limit it to com ports only..
            if (Plugin.Settings.USBCheck)
                   WatchForUSBChanges();

            isReady = true;

            SimHub.Logging.Current.Info("END -> SettingsControl2");
        }

        public void WatchForUSBChanges()
        {
            SimHub.Logging.Current.Info($"Will try to watch for USB events...");
            if (!watcherStarted) {
                string sqlQuery = "SELECT * FROM __InstanceOperationEvent WITHIN 1 WHERE TargetInstance ISA 'Win32_PnPEntity' AND TargetInstance.PNPClass = 'Ports'";

                _watcher = new ManagementEventWatcher();
                _watcher.EventArrived += new EventArrivedEventHandler(USBChangedEvent);
                _watcher.Query = new WqlEventQuery(sqlQuery);
                _watcher.Start();

                watcherStarted = true;
                SimHub.Logging.Current.Info($"Confirmed! watching for USB events");
            }
            else
            {
                SimHub.Logging.Current.Info($"Already doing it! there was no need to load");
            }
        }

        void USBChangedEvent(object sender, EventArrivedEventArgs e)
        {
            string eventType = e.NewEvent.ClassPath.ClassName;
            ManagementBaseObject targetInstance = (ManagementBaseObject)e.NewEvent["TargetInstance"];

            string description = targetInstance["Name"].ToString();
            string portName = System.Text.RegularExpressions.Regex.Match(description, @"(COM\d+)").Value;

            // Debugging
            SimHub.Logging.Current.Info(GTenxor.PrintObjectProperties(sender));
            SimHub.Logging.Current.Info($"BEGIN -> USBChanged for {GTenxor.PluginName} from {sender} for {eventType} on port {portName}");

            string prevSelection;
            bool isChecked;

            switch (eventType)
            {
                case "__InstanceDeletionEvent":
                    Dispatcher.Invoke(async() =>
                    {
                        prevSelection = SerialDevicesComboBox.SelectedItem as string;

                        SimHub.Logging.Current.Info($"Instance DELETE: current COM port is {prevSelection} and {portName} was disconnected");

                        // Do not continue if disconnected serial port is not the same as current.
                        if (prevSelection != portName)
                            return;

                        if (ConnectCheckBox.IsChecked == true)
                            prevDeviceStatePermanent = true;
                        else
                            prevDeviceStatePermanent = false;

                        if (prevDevicePermanent == null)
                            prevDevicePermanent = prevSelection;

                        SimHub.Logging.Current.Info($"Last connected device auto-connect was set to {prevDeviceStatePermanent}");

                        var oldDevices = SerialConnection.SerialDevices.ToList(); // Create a copy of the old device list

                        // Allow time after disconnection to refresh the list
                        await Task.Delay(500);

                        for (int i = 1; i < 6; i++) // Attempt to update the list 5 times
                        {
                            Plugin.SerialConnection.LoadSerialDevices();
                            var newDevices = SerialConnection.SerialDevices;

                            if (!oldDevices.SequenceEqual(newDevices))
                            {
                                SimHub.Logging.Current.Info($"All good! no need to iterate more as a USB change was indeed detected and updated in our list of devices");
                                // The device lists are different, we can stop retrying
                                break;
                            }
                            else
                            {
                                // Interrupt all serial comunication when about to disconnect
                                GTenxor.SerialOK = false;
                                await Task.Delay(500); // Delay to allow for the above variable to update

                                Plugin.SerialConnection.ForcedDisconnect();
                                // Wait for a while before the next try
                                await Task.Delay(1000); // Delay for one second
                                GC.Collect();
                                GC.WaitForPendingFinalizers();
                                await Task.Delay(500); // Delay to allow for garbage collector to do its thing
                                SimHub.Logging.Current.Info($"Serial devices list was not refreshed for {GTenxor.PluginName}, will try again... Attempt Number {i}, let me also force a Disconnect");

                                if (i == 5) // I tried 5 times...
                                {
                                    SimHub.Logging.Current.Info($"Find some extreme methods for {GTenxor.PluginName} as nothing works!");
                                    Plugin.SerialConnection.LoadSerialDevices();
                                }
                            }
                        }

                        SimHub.Logging.Current.Info($"Previous selection was {prevSelection} for {GTenxor.PluginName} (also have {prevDevicePermanent}) and auto connect is {prevDeviceStatePermanent}");

                        SerialDevicesComboBox.ItemsSource = SerialConnection.SerialDevices;
                        string currentSelection = SerialDevicesComboBox.SelectedItem as string;

                        if (currentSelection != prevSelection)
                        {
                            SimHub.Logging.Current.Info($"InstanceDeletionEvent: Here is where we disconnect {GTenxor.PluginName} as it disappeared from my list while being the selected option");
                            Plugin.Settings.SelectedSerialDevice = "None";
                            SerialDevicesComboBox.SelectedItem = "None";
                            ConnectCheckBox.IsChecked = false;
                        }
                        else
                        {
                            SimHub.Logging.Current.Info($"Do we have to re-connect {GTenxor.PluginName}?");
                            ConnectCheckBox.IsChecked = false;
                            await Task.Delay(1000); // Delay to allow for the above variable to update
                            ConnectCheckBox.IsChecked = true;
                            await Task.Delay(1000); // Delay to allow for the above variable to update

                            GTenxor.SerialOK = true;
                        }
                        SimHub.Logging.Current.Info($"DISCONNECT: The last connected device was {prevDevicePermanent} and Connect Checkbox was set to {prevDeviceStatePermanent}");
                    });
                    break;
                case "__InstanceCreationEvent":
                    Dispatcher.Invoke(() =>
                    {
                        prevSelection = SerialDevicesComboBox.SelectedItem as string;

                        SimHub.Logging.Current.Info($"Instance CREATE: current COM port is {prevSelection} and {portName} was connected");

                        // Do not continue if the connected serial port and current are not the same and current is NOT set to None.
                        if (prevSelection != portName && prevSelection != "None" && prevSelection != null)
                            return;

                        // Now load the devices
                        if (prevDeviceStatePermanent == true)
                            isChecked = true;
                        else
                            isChecked = false;

                        // Allow time after re-connection to refresh the list
                        Thread.Sleep(1000);

                        SerialConnection.LoadSerialDevices();
                        SerialDevicesComboBox.ItemsSource = SerialConnection.SerialDevices;

                        bool SelectedDev = SerialDevicesComboBox.SelectedItem is string selectedDevice;
                        if (SelectedDev)
                        {
                            if (prevDevicePermanent != null)
                            {
                                SerialDevicesComboBox.SelectedItem = prevDevicePermanent;
                            }
                            ConnectCheckBox.IsChecked = isChecked;
                        }
                        else
                        {
                            SimHub.Logging.Current.Info($"InstanceCreationEvent: Here is where we disconnect {GTenxor.PluginName}");
                            Plugin.Settings.SelectedSerialDevice = "None";
                            SerialDevicesComboBox.SelectedItem = "None";
                            ConnectCheckBox.IsChecked = false;
                            Plugin.Settings.ConnectToSerialDevice = false;
                        }
                        SimHub.Logging.Current.Info($"CONNECT: The last connected device was {prevDevicePermanent} and Connect Checkbox was set to {isChecked}");
                    });
                    break;
            }
            SimHub.Logging.Current.Info("END -> USBChanged");
        }

        private void SerialConnection_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            SimHub.Logging.Current.Info($"BEGIN -> PropertyChanged for {GTenxor.PluginName} from {sender}");

            if (e.PropertyName == nameof(SerialConnection.SerialDevices))
            {
                string prevDevice;
                bool prevDeviceState;

                if(prevDeviceStatePermanent)
                    ConnectCheckBox.IsChecked = false;

                if (prevDevicePermanent != null)
                    prevDevice = prevDevicePermanent;
                else
                    prevDevice = SerialDevicesComboBox.SelectedItem as string;

                if (prevDeviceStatePermanent)
                    prevDeviceState = prevDeviceStatePermanent;
                else
                    prevDeviceState = (bool)ConnectCheckBox.IsChecked;

                // Update the ComboBox items when SerialDevices property changes
                // Now load the devices
                SerialDevicesComboBox.ItemsSource = SerialConnection.SerialDevices;

                if (SerialDevicesComboBox.SelectedItem is string)
                {
                    // The selected item is of type string, no need to assign it to a variable
                }
                else
                {
                    Plugin.Settings.SelectedSerialDevice = "None";
                    SerialDevicesComboBox.SelectedItem = "None";
                    if (prevDevice != null && prevDevice != "None")
                    {
                        // Set PrevDevice as a shared value so that you can later attempt a reconnect to the same COM port
                        prevDevicePermanent = prevDevice;
                    }
                    prevDeviceStatePermanent = prevDeviceState;
                }
            }
            if (e.PropertyName == nameof(SerialConnection.SelectedBaudRate))
            {
                if (BaudRateComboBox.SelectedItem is string _selectedBaudRate)
                {
                    Plugin.Settings.SelectedBaudRate = _selectedBaudRate;
                    SimHub.Logging.Current.Info($"Updated Plugin.Settings.SelectedBaudRate with {_selectedBaudRate}");
                    SerialConnection.SelectedBaudRate = Plugin.Settings.SelectedBaudRate;
                }
            }
            SimHub.Logging.Current.Info("END -> SerialConnection_PropertyChanged");
        }

        private void SettingsControl_Loaded(object sender, RoutedEventArgs e)
        {
            SimHub.Logging.Current.Info("BEGIN -> SettingsControl_Loaded");
            if (SerialConnection != null)
            {
                if (SerialConnection.IsConnected) { 
                    SimHub.Logging.Current.Info("Already connected!");
                }
                else { 
                    AutoDetectDevice(Plugin.Settings); 
                }
            }
            SimHub.Logging.Current.Info("END -> SettingsControl_Loaded");
        }
        private void SettingsControl_Unloaded(object sender, RoutedEventArgs e)
        {
            SimHub.Logging.Current.Info("BEGIN -> SettingsControl_UnLoaded");
            SimHub.Logging.Current.Info("END -> SettingsControl_UnLoaded");
        }

        private async void Button_Click(object sender, RoutedEventArgs e)
        {
            await Task.Delay(1);
            string portName = Plugin.Settings.SelectedSerialDevice;
            SimHub.Logging.Current.Info($"Currently selected port is {portName}");

            if (sender == AutodetectButton)
            {
                if (AlreadyConnectedError())
                    return;

                AutoDetectDevice(Plugin.Settings);
            }

            /*
            else if (sender == ToggleButton)
            {
                // This is just to send the string to change the device state
                await ChangeDeviceState(GTenxor.Constants.HandShakeSnd, portName);
            }
            else if (sender == ToggleButton2)
            {
                // This is just to send the string to change the device state
                await ChangeDeviceState(GTenxor.Constants.uniqueID, portName);
            }
            else if (sender == Debugeador)
            {
                // This is just to send the string to Query device state
                await ChangeDeviceState(GTenxor.Constants.QueryStatus, portName);

                // This is just to send the string to change the device state
                string _data = GTenxor.PrintObjectProperties(Plugin.Settings);
                OutputMsg(_data);
            }
            */

        }

        // The following are the actual events we just created above in settings control
        // Event handler for SerialDevicesComboBox selection change
        private async void ComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender == SerialDevicesComboBox)
            {
                ConnectCheckBox.IsChecked = false;
                Plugin.Settings.ConnectToSerialDevice = false;
                await SerialConnection.Disconnect(withoutDelay: true);
            }
            if (sender == BaudRateComboBox)
            {
                ConnectCheckBox.IsChecked = false;
                await SerialConnection.Disconnect(withoutDelay: true);
            }
        }

        // Event handler for ConnectCheckBox checked
        private async void Connect_Checked(object sender, EventArgs e)
        {
            SimHub.Logging.Current.Info("BEGIN -> Connect_Checked from sender: " + sender);

            string currentSelection = SerialDevicesComboBox.SelectedItem as string;
            int BaudRate = int.Parse(Plugin.Settings.SelectedBaudRate);

            SimHub.Logging.Current.Info($"Read the BaudRate from Settings as {BaudRate}");

            try
            {
                if (currentSelection == "None")
                {
                    ConnectCheckBox.IsChecked = false;
                    string _data = "You need to select a port";
                    OutputMsg(_data);
                }
                else
                {
                    // Try Connecting using the same method from gTenxor...    
                    if (await TryConnect(currentSelection,BaudRate))
                    {
                        Plugin.Settings.ConnectToSerialDevice = true;
                        ConnectCheckBox.IsChecked = true;
                    }
                    else
                    {
                        Plugin.Settings.ConnectToSerialDevice = false;
                        ConnectCheckBox.IsChecked = false;
                        string _data = "Device already in use.\nRestart SimHub and try again";
                        OutputMsg(_data);
                    }
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                SimHub.Logging.Current.Error($"Access to the port '{currentSelection}' is denied. " + ex.Message);
            }
            catch (Exception)
            {
                SimHub.Logging.Current.Error($"Could not connect to {currentSelection}");
            }

            SimHub.Logging.Current.Info("END -> Connect_Checked");
        }

        // Event handler for ConnectCheckBox unchecked
        private async void Connect_Unchecked(object sender, EventArgs e)
        {
            // Disconnect from the selected serial device.
            Plugin.Settings.ConnectToSerialDevice = false;
            MaxTest.IsChecked = false;
            await SerialConnection.Disconnect(withoutDelay: true);
        }

        // Event handler for ConnectCheckBox checked
        private void CheckBox_Checked(object sender, EventArgs e)
        {
            if (sender == MaxTest)
            {
                if (ConnectCheckBox.IsChecked == false)
                {
                    string _data = "You need to be connected first!";
                    OutputMsg(_data);
                    MaxTest.IsChecked = false;
                    return;
                }

                LeftOffset.IsEnabled = false;
                RightOffset.IsEnabled = false;
                DecelGain.IsEnabled = false;
                YawGain.IsEnabled = false;
                Smooth.IsEnabled = false;

                SerialCommand(sender);
            }
        }

        // Event handler for ConnectCheckBox unchecked
        private void CheckBox_Unchecked(object sender, EventArgs e)
        {
            // Update settings based on the data that was changed
            if (sender == MaxTest)
            {
                LeftOffset.IsEnabled = true;
                RightOffset.IsEnabled = true;
                DecelGain.IsEnabled = true;
                YawGain.IsEnabled = true;
                Smooth.IsEnabled = true;

                SerialCommand(sender);
            }
        }

        private void Slider_ValueChanged(object sender, EventArgs e)
        {
            if (sender == LeftOffset || sender == RightOffset || sender == Tmax)
            {
                // Here's the actual command to send to my device
                SerialCommand(sender);
            }
        }
        private void SerialCommand(object sender)
        {
            // If my environment loaded already...
            if (!isReady)
                return;

            // Not connected? don't even run...
            if (!SerialConnection.IsConnected)
                return;

            // Doing things right now!
            byte _0 = 0;
            byte _L = (byte)Plugin.Settings.LeftOffset;
            byte _1 = 1;
            byte _R = (byte)Plugin.Settings.RightOffset;
            byte _Two = 2;
            byte _Three = 3;
            byte _tmax = (byte)Plugin.Settings.Tmax;

            if (Plugin.Settings.MaxTest)
            {
                _Two = (byte)((_tmax - 1) - _L);
                _Three = (byte)(_Two + 1);
            }

            _Two = Math.Max(Math.Min(_Two, _tmax), (byte)2);
            _Three = Math.Max(Math.Min(_Three, _tmax), (byte)3);

            // Convert to byte
            byte[] serialData = new byte[] { _0, _L, _1, _R, _Two, _Three };

            if (!Plugin.Settings.MaxTest && sender == Tmax)
            {
                // Just avoid sending serial commands for this scenario.
            }
            else
            {
                // SimHub.Logging.Current.Info("I'm about to send the command via SerialCommand: " + command);
                SerialConnection.SerialPort.Write(serialData, 0, serialData.Length);
            }
        }

        public async Task<bool> TryConnect(string currentSelection, int BaudRate)
        {
            SimHub.Logging.Current.Info("BEGIN -> TryConnect");
            bool ConnectToDevice = Plugin.Settings.ConnectToSerialDevice;
            string selectedPort = currentSelection ?? Plugin.Settings.SelectedSerialDevice;

            // Fucking Comboboxes los odio
            int SelectedBaudRate;
            string _SelectedBaudRate = Plugin.Settings.SelectedBaudRate;

            if (BaudRate == 0)
                SelectedBaudRate = int.Parse(_SelectedBaudRate);
            else
                SelectedBaudRate = BaudRate;

            SimHub.Logging.Current.Info($"Autoconnect is {ConnectToDevice} on port {selectedPort} using the following BaudRate {SelectedBaudRate}");

            if (currentSelection != null) {
                if (!string.IsNullOrEmpty(currentSelection) && currentSelection != "None")
                {
                    selectedPort = currentSelection;
                    ConnectToDevice = true;
                }
            }
            bool isChecked = false;
                try
                {
                    if (ConnectToDevice && !string.IsNullOrEmpty(selectedPort) && selectedPort != "None") {
                        SimHub.Logging.Current.Info($"Will run TryConnect() now and connect at {SelectedBaudRate}");

                        // Disconnect and dispose of the old connection if it exists.
                        if (SerialConnection == null)
                        {
                            SerialConnection = new SerialConnection();
                        }

                        if(Plugin.SerialConnection == null)
                            Plugin.SerialConnection = SerialConnection;

                        if (SerialConnection.IsConnected)
                        {
                            SimHub.Logging.Current.Info("And it was a success (was already connected)");
                            SerialConnection.ChangeDeviceStateAsync(GTenxor.Constants.uniqueID);
                            SimHub.Logging.Current.Info("Our device should be ready to receive data...");
                            isChecked = true;
                            int CurrentbaudRate = SerialConnection.GetBaudRate();
                            SimHub.Logging.Current.Info($"Connected at {CurrentbaudRate} baud rate");
                        }
                        else
                        {
                        // Now connect to the new port
                            await SerialConnection.Connect(selectedPort, SelectedBaudRate, ResetCon: true);
                            SimHub.Logging.Current.Info("Will try to connect to: " + selectedPort);
                            if(SerialConnection.IsConnected) {
                                SimHub.Logging.Current.Info("And it was a success (Created a new connection)");
                                SerialConnection.ChangeDeviceStateAsync(GTenxor.Constants.uniqueID);
                                SimHub.Logging.Current.Info("Our device should be ready to receive data...");
                                isChecked = true;
                                int CurrentbaudRate = SerialConnection.GetBaudRate();
                                SimHub.Logging.Current.Info($"Connected at {SelectedBaudRate} baud rate");
                                InitServos();
                        }
                            else {
                                SimHub.Logging.Current.Info("But failed...");
                                // Display a modal dialog with a message
                                Plugin.Settings.ConnectToSerialDevice = false;
                                Plugin.Settings.SelectedSerialDevice = "None";
                                ConnectCheckBox.IsChecked = false;
                                SimHub.Logging.Current.Info("Not Connected");
                                isChecked = false;
                                InitServos();
                        }
                        }
                    }
                    else { 
                        SimHub.Logging.Current.Info($"Did NOT run TryConnect(), and value for ConnectToDevice is {ConnectToDevice} and port is {selectedPort} and speed was {SelectedBaudRate}");
                }
                }
                catch (UnauthorizedAccessException ex)
                {
                    SimHub.Logging.Current.Error($"Access to the port '{selectedPort}' is denied. " + ex.Message);

                    Plugin.Settings.ConnectToSerialDevice = false;
                    Plugin.Settings.SelectedSerialDevice = "None";
                    ConnectCheckBox.IsChecked = false;
                    isChecked = false;
                }
                catch (Exception ex)
                {
                    // Handle the exception
                    // Display a message, log an error, or perform any necessary actions
                    SimHub.Logging.Current.Error("An error occurred during serial connection: " + ex.Message);

                    Plugin.Settings.ConnectToSerialDevice = false;
                    Plugin.Settings.SelectedSerialDevice = "None";
                    ConnectCheckBox.IsChecked = false;
                    isChecked = false;
                }
            SimHub.Logging.Current.Info("END -> TryConnect");
            return isChecked;
        }

        public void InitServos()
        {
            // Initialize the servos
            byte _0 = 0;
            byte _L = (byte)Plugin.Settings.LeftOffset;
            byte _1 = 1;
            byte _R = (byte)Plugin.Settings.RightOffset;
            byte _Two = 2;
            byte _Three = 3;
            byte _tmax = (byte)Plugin.Settings.Tmax;

            _Two = Math.Max(Math.Min(_Two, _tmax), (byte)2);
            _Three = Math.Max(Math.Min(_Three, _tmax), (byte)3);

            // Convert to byte
            byte[] serialData = new byte[] { _0, _L, _1, _R, _Two, _Three };

            SerialConnection.SerialPort.Write(serialData, 0, serialData.Length);
        }

        public async Task ChangeDeviceState(string identifier, string portName)
        {
            int BaudRate = int.Parse(BaudRateComboBox.SelectedItem.ToString());

            // If SerialConnection is null, instantiate it.
            if (SerialConnection == null)
            {
                SimHub.Logging.Current.Info("Instantiating SerialConnection for ChangeDeviceState");
                SerialConnection = new SerialConnection();
            }
            else
            {
                SimHub.Logging.Current.Info("Using already instantiated SerialConnection while running ChangeDeviceState");
            }

            SimHub.Logging.Current.Info("About to ask if IsConnected");

            bool use_existing;
            if (SerialConnection.IsConnected)
            {
                use_existing = true;
                OutputMsg("Using existing connection to " + portName);
                SimHub.Logging.Current.Info("Using existing connection to " + portName);
            }
            else
            {
                use_existing = false;
                try
                {
                    await SerialConnection.Connect(portName, BaudRate, ResetCon: true);

                    if (!SerialConnection.IsConnected)
                        return;


                    OutputMsg("Just opened a new connection to " + portName);
                    SimHub.Logging.Current.Info("Just opened a new connection to " + portName);
                    SimHub.Logging.Current.Info("Waited for 2 seconds...");
                    OutputMsg("Waited 2 seconds, lets send commands");
                }
                catch (UnauthorizedAccessException ex)
                {
                    SimHub.Logging.Current.Error($"Access to the port '{portName}' is denied. " + ex.Message);
                    OutputMsg($"Access to the port '{portName}' is denied. " + ex.Message);
                }
                catch (Exception)
                {
                    SimHub.Logging.Current.Error($"Could not connect to {portName}");
                    OutputMsg($"Could not connect to {portName}");
                }
                finally
                {
                    // SimHub.Logging.Current.Error($"Something happened!");
                    // OutputMsg($"Something happened!");
                }
            }

            if (SerialConnection.SerialPort == null)
                return;

            // Handle it here
            byte[] command;
            if (identifier == GTenxor.Constants.QueryStatus) // If Only query set Byte 254
                command = new byte[] { 254 };
            else
                command = new byte[] { 255 };

            SimHub.Logging.Current.Info($"Command {command[0]} sent with {identifier}!");
            OutputMsg(($"Command {command[0]} sent with {identifier}!"));

            // Send the action Command
            SerialConnection.SerialPort.Write(command, 0, 1);
            SerialConnection.SerialPort.WriteLine(identifier);

            // Wait a bit before disconnecting... an only disconnect IF we created a new connection
            if(use_existing == false)
                await SerialConnection.Disconnect();
        }
        public void HandleDataReceived(string data)
        {
            SimHub.Logging.Current.Info($"Received: {data.Trim()}");

            // Process the received data, e.g., check if it meets your criteria
            if (data.Trim() == GTenxor.Constants.uniqueIDresponse)
            {
                SimHub.Logging.Current.Info($"Received ACK! We are ready to send via serial");
                // Set SerialOK 
                GTenxor.SerialOK = true;
            }
            Application.Current.Dispatcher.Invoke(() => { OutputMsg(data); });
        }
        private async void HandleReadySerialPortFound(SerialConnection readySerialConnection)
        {
            await semaphore.WaitAsync();
            try
            {
                if (readySerialConnection.SerialPort != null)
                {
                    string _data = "Found gTenxor on " + readySerialConnection.SerialPort.PortName;
                    // Perform necessary actions with the ready serial port
                    Application.Current.Dispatcher.Invoke(() => {
                        OutputMsg(_data);
                        SerialDevicesComboBox.SelectedItem = readySerialConnection.SerialPort.PortName;
                    }); 
                }
            }
            catch (Exception ex)
            {
                SimHub.Logging.Current.Error($"Error handling ready port: {ex.Message}");
            }
            finally
            {
                // Always release the semaphore.
                semaphore.Release();
            }

            // If we successfully processed the port, disconnect and inform the user.
            if (readySerialConnection.SerialPort != null && SerialConnection.IsConnected)
            {
                try
                {
                    await readySerialConnection.Disconnect();
                    OutputMsg("Closed port " + readySerialConnection.SerialPort.PortName);
                    SimHub.Logging.Current.Info("Closed port " + readySerialConnection.SerialPort.PortName);
                }
                catch (Exception ex)
                {
                    SimHub.Logging.Current.Error($"Error disconnecting port: {ex.Message}");
                }
            }
        }
        public void OutputMsg(string data)
        {
            DebugOutputTextBox.AppendText(data);
            DebugOutputTextBox.AppendText(Environment.NewLine); // Optional: Add a new line after each new data
            DebugOutputTextBox.ScrollToEnd(); // Optional: Scroll to the end of the text box
        }
        private bool AlreadyConnectedError()
        {
            bool isError = false;
            if (ConnectCheckBox.IsChecked == true)
            {
                isError = true;
                string _data = "A Device is already connected!\nDisconnect it, reset your device and try again\n";
                OutputMsg(_data);
            }
            return isError;
        }
        public async void AutoDetectDevice(MainSettings Settings)
        {
            // Check all ports syncronously and fast
            DeviceAutoDetect autoDetect = new DeviceAutoDetect();

            try
            {
                AutodetectButton.IsEnabled = false;
                DeviceAutoDetect.ReadySerialPortFound += HandleReadySerialPortFound;
                await autoDetect.CheckSerialPort(Settings);
            }
            catch (Exception ex)
            {
                SimHub.Logging.Current.Error($"Process was terminated: {ex.Message}");
            }
            finally
            {
                AutodetectButton.IsEnabled = true;
                DeviceAutoDetect.ReadySerialPortFound -= HandleReadySerialPortFound;
            }
        }
    }
}