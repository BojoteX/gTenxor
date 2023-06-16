using GameReaderCommon;
using SimHub.Plugins;
using System;
using System.Reflection;
using System.Windows.Media;

namespace Bojote.gTenxor
{
    [PluginDescription("Controls a seat belt/harness based on telemetry data to simulate g-Forces")]
    [PluginAuthor("Bojote")]
    [PluginName("gTenxor")]

    public class Main : IPlugin, IDataPlugin, IWPFSettingsV2
    {
        public static class Constants
        {
            public const string HandShakeSnd        = "Tenxor_OK";
            public const string HandShakeRcv        = "BINGO";
            public const string uniqueID            = "PUTINESGAY";
            public const string uniqueIDresponse    = "ACK";
            public const string QueryStatus         = "KNOCK-KNOCK";
        }

        public SerialConnection SerialConnection { get; set; }

        // Declared for gameData
        public static bool SerialOK = false;

        // Low Pass Filter variables for servo operation
        double prevDecel = 0;
        double prevSway = 0;

        public MainSettings Settings;

        /// <summary>
        /// Instance of the current plugin manager
        /// </summary>
        public PluginManager PluginManager { get; set; }

        /// <summary>
        /// Gets the left menu icon. Icon must be 24x24 and compatible with black and white display.
        /// </summary>
        public ImageSource PictureIcon => this.ToIcon(Properties.Resources.pluginicon);

        /// <summary>
        /// Gets a short plugin title to show in left menu. Return null if you want to use the title as defined in PluginName attribute.
        /// </summary>
        public string LeftMenuTitle => "gTenxor Settings";

        /// <summary>
        /// Called one time per game data update, contains all normalized game data,
        /// raw data are intentionnally "hidden" under a generic object type (A plugin SHOULD NOT USE IT)
        ///
        /// This method is on the critical path, it must execute as fast as possible and avoid throwing any error
        ///
        /// </summary>
        /// <param name="pluginManager"></param>
        /// <param name="data">Current game data, including current and previous data frame.</param>
        public void DataUpdate(PluginManager pluginManager, ref GameData data)
        {
            // Define the value of our property (declared in init)
            if (data.GameRunning)
            {
                if (data.OldData != null && data.NewData != null)
                {
                    if (SerialOK && SerialConnection.IsConnected)
                    {
                        // Tmax should never be a number over 180;
                        int tmax = Settings.Tmax;

                        // Reverse the direcction of Surge or Sway if requested (and apply a gain factor)
                        double decel = Settings.DecelGain * (data.NewData.AccelerationSurge ?? 0) * (Settings.DecelReversed ? -1 : 1);
                        double sway = Settings.YawGain * (data.NewData.AccelerationSway ?? 0) * (Settings.SwayReversed ? -1 : 1);

                        // Adjust the filter strength from 0 (no filtering) to 10 (heavy filtering)
                        int filterStrength = Settings.Smooth;
                        double alpha = 2.0 / (filterStrength + 1.0);

                        // Apply the low-pass filter
                        double decelFiltered = prevDecel + alpha * (decel - prevDecel);
                        double swayFiltered = prevSway + alpha * (sway - prevSway);

                        prevDecel = decelFiltered;
                        prevSway = swayFiltered;

                        // Calculating the hypotenuse of the surge and sway forces
                        double decelSquared = decelFiltered * decelFiltered;
                        double swaySquared = swayFiltered * swayFiltered;
                        double decelDoubled = decelFiltered * 2;

                        // Compute l and r
                        double r = Math.Sqrt(decelSquared + swaySquared);
                        double l = decelDoubled - r;

                        // Apply deadzone to the servo positions
                        double deadzoneRange = Settings.Deadzone; // Adjust this value based on your desired deadzone range
                        double deadzoneCenter = 0.0; // Adjust this value if you want the deadzone centered around a different position

                        // Apply deadzone to the left servo position
                        if (Math.Abs(l) <= deadzoneCenter + deadzoneRange / 2.0)
                        {
                            l = deadzoneCenter;
                        }

                        // Apply deadzone to the right servo position
                        if (Math.Abs(r) <= deadzoneCenter + deadzoneRange / 2.0)
                        {
                            r = deadzoneCenter;
                        }

                        // Swap if necessary (be careful + or - values have an effect on which servo is being affected)
                        if (swayFiltered < 0)
                        {
                            (r, l) = (l, r);
                        }

                        // Clipping so that l and r are never > tmax or less than the numbers 2 or 3
                        l = Math.Max(Math.Min(l, tmax), 2);
                        r = Math.Max(Math.Min(r, tmax), 3);

                        // For future use. Trigger an event if both l and r are at tmax
                        if (l == tmax && r == tmax)
                        {
                            this.TriggerEvent("MaxTension");
                        }

                        byte leftServoAngle = (byte)Math.Round(l);
                        byte rightServoAngle = (byte)Math.Round(r);

                        byte[] serialData = new byte[] { leftServoAngle, rightServoAngle };
                        SerialConnection.SerialPort.Write(serialData, 0, 2);
                    }
                }
            }
        }

        /// <summary>
        /// Called at plugin manager stop, close/dispose anything needed here !
        /// Plugins are rebuilt at game change
        /// </summary>
        /// <param name="pluginManager"></param>
        public void End(PluginManager pluginManager)
        {
            SimHub.Logging.Current.Info("BEGIN -> End");

            // Save settings
            this.SaveCommonSettings("gTenxorGeneral", Settings);

            if (SerialConnection != null)
            {
                // Plugin.Settings.ConnectToSerialDevice = false;
                SerialConnection.ForcedDisconnect();
                SimHub.Logging.Current.Info("Changed game, disconnecting serial!");
            }

            SimHub.Logging.Current.Info("END -> End");
        }

        /// <summary>
        /// Returns the settings control, return null if no settings control is required
        /// </summary>
        /// <param name="pluginManager"></param>
        /// <returns></returns>
        public System.Windows.Controls.Control GetWPFSettingsControl(PluginManager pluginManager)
        {
            return new SettingsControl(this);
        }

        /// <summary>
        /// Called once after plugins startup
        /// Plugins are rebuilt at game change
        /// </summary>
        /// <param name="pluginManager"></param>
        public void Init(PluginManager pluginManager)
        {
            // Start Logging
            SimHub.Logging.Current.Info("Starting gTenxor Plugin...");

            SimHub.Logging.Current.Info("BEGIN -> Init");

            // Load settings
            Settings = this.ReadCommonSettings<MainSettings>("gTenxorGeneral", () => CreateDefaultSettings() );

            if (SerialConnection == null)
            {
                SimHub.Logging.Current.Info($"Will create a new SerialConnection() object");
                SerialConnection = new SerialConnection();
            }
            else
            {
                SimHub.Logging.Current.Info($"A SerialConnection() already existed, we'll use that one");
            }

            // Declare a property available in the property list, this gets evaluated "on demand" (when shown or used in formulas)
            this.AttachDelegate("CurrentDateTime", () => DateTime.Now);

            // Declare an action which can be called
            this.AddAction("ToggleLiveBelt", (a, b) =>
            {
                if (SerialOK)
                    SerialOK = false;
                else
                    SerialOK = true;
            });

            // Declare an event
            this.AddEvent("MaxTension");

            // Declare an action which can be called
            this.AddAction("IncrementMaxTension",(a, b) =>
            {
            Settings.Tmax++;
            });

            // Declare an action which can be called
            this.AddAction("DecrementMaxTension", (a, b) =>
            {
                Settings.Tmax--;
            });

            SimHub.Logging.Current.Info("END -> Init");
        }

        // When loading for the first time if not settings exists we load the following
        private MainSettings CreateDefaultSettings()
        {
            // Create a new instance of MainSettings and set default values
            var settings = new MainSettings
            {
                SelectedSerialDevice = "None",
                ConnectToSerialDevice = false,
                SelectedBaudRate = "115200",
                Deadzone = 5,
                LeftOffset = 15,
                RightOffset = 15,
                Tmax = 180,
                DecelGain = 5,
                YawGain = 5,
                Smooth = 10,
                MaxTest = false,
                SwayReversed = false,
                DecelReversed = false
            };
            return settings;
        }
        
        public static string PrintObjectProperties(object obj)
        {
            Type objType = obj.GetType();
            PropertyInfo[] properties = objType.GetProperties();

            string _value = null;
            foreach (PropertyInfo property in properties)
            {
                object value = property.GetValue(obj, null);
                _value += ($"{property.Name} => {value}\n");
            }
            return _value;
        }

    }
}