namespace Zebble.Device
{
    using System;

    public partial class MediaCaptureSettings
    {
        public VideoQuality VideoQuality = VideoQuality.High;
        public CameraOption Camera = CameraOption.Rear;

        public TimeSpan? VideoMaxDuration;
        public bool AllowEditing = true;

        public Func<object> OverlayViewProvider { get; set; }
    }
}
