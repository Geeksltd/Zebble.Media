namespace Zebble.Device
{
    using System;

    public partial class MediaCaptureSettings
    {
        public VideoQuality VideoQuality = VideoQuality.High;
        public CameraOption Camera = CameraOption.Rear;

        public TimeSpan? VideoMaxDuration;
        public bool AllowEditing = true;
        public bool PurgeCameraRoll = false;

        public Func<object> OverlayViewProvider { get; set; }
    }
}
