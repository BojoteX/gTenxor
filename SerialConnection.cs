using System;
using System.IO.Ports;
using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Bojote.gTenxor
{
    public class SerialConnection : INotifyPropertyChanged, IDisposable
    {
        private readonly SemaphoreSlim semaphore = new SemaphoreSlim(1, 1);
        public delegate void DataReceivedHandler(string data);
        public event DataReceivedHandler OnDataReceived;
        public event PropertyChangedEventHandler PropertyChanged;
        private bool disposed = false;

        public SerialPort SerialPort { get; set; }
        public bool IsConnected => SerialPort?.IsOpen == true;

        private ObservableCollection<string> serialDevices;
        public ObservableCollection<string> SerialDevices
        {
            get { return serialDevices; }
            set
            {
                if (serialDevices != value)
                {
                    serialDevices = value;
                    OnPropertyChanged(nameof(SerialDevices));
                }
            }
        }

        public List<string> BaudRates { get; } = new List<string> { "9600", "115200" };
        
        private string _selectedBaudRate;
        public string SelectedBaudRate
        {
            get { return _selectedBaudRate; }
            set
            {
                _selectedBaudRate = value;
                OnPropertyChanged(nameof(SelectedBaudRate)); // Raises PropertyChanged event
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~SerialConnection()
        {
            Dispose(false);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposed)
            {
                if (disposing)
                {
                    // Dispose managed state (managed objects)
                    SerialPort?.Close();
                    SerialPort?.Dispose();
                    semaphore.Dispose();
                    // set large fields to null
                    SerialPort = null;
                }

                disposed = true;
            }
        }

        public void LoadSerialDevices()
        {
            string[] portNames = SerialPort.GetPortNames();
            foreach (string port in portNames)
            {
                SimHub.Logging.Current.Info("Serial Port: " + port);
            }
            SerialDevices = new ObservableCollection<string>(portNames);
            SerialDevices.Insert(0, "None");
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public int GetBaudRate()
        {
            if (SerialPort != null)
            {
                return SerialPort.BaudRate;
            }
            else
            {
                throw new InvalidOperationException("Serial port is not initialized.");
            }
        }

        public async Task<SerialConnection> Connect(string portName, int BaudRate = 115200, bool ResetCon = true)
        {
            if (IsConnected)
            {
                await Task.Delay(2000);
                SimHub.Logging.Current.Info($"Was already connected! still waited 2 seconds to allow Device to respond to requests");
                Main.SerialOK = true;
                return this;
            }

            int actualBaudRate;
            if (int.TryParse(SelectedBaudRate, out int _SelectedBaudRate))
            {
                // Parse successful, _SelectedBaudRate now holds the integer value of the string
                actualBaudRate = _SelectedBaudRate;
            }
            else
            {
                actualBaudRate = BaudRate;
            }

            SimHub.Logging.Current.Info($"Will attempt connection use as actualBaudrate {actualBaudRate} (I received {_SelectedBaudRate} as _SelectedBaudRate and {BaudRate} as BaudRate). Important for future troubleshooting");

            SerialPort = new SerialPort(portName, actualBaudRate)
            {
                RtsEnable = ResetCon,
                DtrEnable = ResetCon,
                Parity = Parity.None,
                DataBits = 8,
                StopBits = StopBits.One,
                Handshake = Handshake.None,
                ReadTimeout = 5,
                WriteTimeout = 5,
                ReadBufferSize = 64, // 4096 is default
                WriteBufferSize = 64 // 2048 is default
            };

            SerialPort.DataReceived -= SerialPort_DataReceived;
            SerialPort.DataReceived += SerialPort_DataReceived;

            try
            {
                SerialPort.Open();
                if(SerialPort.IsOpen)
                {
                    await Task.Delay(2000);
                    SimHub.Logging.Current.Info($"Connected! and waited 2 seconds to allow Device to respond to requests");
                    Main.SerialOK = true;
                    return this;
                }
                else
                {
                    SimHub.Logging.Current.Error($"Could not connect! check the logs");
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                SimHub.Logging.Current.Error($"Access to the port '{portName}' is denied. " + ex.Message);
                return null;
            }
            catch (Exception)
            {
                SimHub.Logging.Current.Error($"Could not connect to {portName}");
                return null;
            }
            return null;
        }

        public void ForcedDisconnect()
        {
            if (!IsConnected)
                return;

            try
            {
                if (SerialPort == null)
                    return;

                Main.SerialOK = false;
                SerialPort.Close();
            }
            finally
            {
                if (SerialPort != null)
                {
                    SerialPort.Dispose();
                    SerialPort = null;
                    Main.SerialOK = false;
                }
            }
        }

        public async Task Disconnect(bool withoutDelay = false)
        {
            if (!IsConnected)
                return;

            if (SerialPort == null)
                return;

            // Always allow for some time for data in the buffer to arrive in time for DataReceived 
            if(!withoutDelay)
                await Task.Delay(3000);

            try
            {
                if (SerialPort == null)
                    return;

                Main.SerialOK = false;
                SerialPort.Close();
            }
            finally
            {
                if (SerialPort != null)
                {
                    SerialPort.Dispose();
                    SerialPort = null;
                    Main.SerialOK = false;
                }
            }
        }

        public void ChangeDeviceStateAsync(string identifier)
        {
            if (IsConnected)
            {
                if (SerialPort == null)
                    return;

                byte[] command255 = { 255 };
                SerialPort.Write(command255, 0, 1);
                SerialPort.WriteLine(identifier);

                Thread.Sleep(10);

            }
            else
            {
                SimHub.Logging.Current.Error("Serial port is not open yet...");
            }
        }

        public async void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                await semaphore.WaitAsync();

                if (!SerialPort.IsOpen)
                {
                    return;
                }

                SerialPort sp = (SerialPort)sender;
                string data = null;

                if (sp != null && sp.IsOpen)
                {
                    data = sp.ReadExisting();
                }

                OnDataReceived?.Invoke(data);
            }
            catch (Exception ex)
            {
                SimHub.Logging.Current.Error(ex.Message);
            }
            finally
            {
                semaphore.Release();
            }
        }
    }
}