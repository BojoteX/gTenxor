namespace Bojote.gTenxor
{
    public class MainSettings
    {
        public string SelectedSerialDevice { get; set; }
        public string SelectedBaudRate { get; set; }
        public bool ConnectToSerialDevice { get; set; }
        public int LeftOffset { get; set; }
        public int RightOffset { get; set; }
        public int Tmax { get; set; }
        public int DecelGain { get; set; }
        public int YawGain { get; set; }
        public int Smooth { get; set; }
        public bool MaxTest { get; set; }
        public bool SwayReversed { get; set; }
        public bool DecelReversed { get; set; }
        public bool USBCheck { get; set; }
    }
}