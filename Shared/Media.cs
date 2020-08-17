namespace Zebble.Device
{
    using System;
    using System.IO;
    using System.Threading.Tasks;

    public partial class Media : Button, FormField.IControl, IBindableInput
    {
        public readonly AsyncEvent SelectedFileChanged = new AsyncEvent(ConcurrentEventRaisePolicy.Queue);
        object FormField.IControl.Value { get => SelectedFile; set => SelectedFile = (FileInfo)value; }

        FileInfo @selectedFile;
        public FileInfo SelectedFile
        {
            get => @selectedFile;
            set
            {
                if (@selectedFile == value) return;
                @selectedFile = value;
                SelectedFileChanged.Raise();
            }
        }

        public void AddBinding(Bindable bindable) => SelectedFileChanged.Handle(() => bindable.SetUserValue(SelectedFile));

        public override void Dispose()
        {
            SelectedFileChanged?.Dispose();
            base.Dispose();
        }

        /// <summary>Saves a picked photo file into a local temp folder in the device's cache folder and returns it.</summary>
        public async Task<FileInfo> TakePhoto(Device.MediaCaptureSettings settings = null, OnError errorAction = OnError.Alert)
        {
            if (!await IsCameraAvailable())
            {
                await errorAction.Apply("No available camera was found on this device.");
                return null;
            }

            if (!SupportsTakingPhoto())
            {
                await errorAction.Apply("Your device does not seem to support taking photos.");
                return null;
            }

            if (!await Device.Permission.Camera.IsRequestGranted())
            {
                await SuggestLaunchingSettings(errorAction, "Permission was denied to access the camera.");
                return null;
            }

            try
            {
                selectedFile = await Thread.UI.Run(() => DoTakePhoto(settings ?? new Device.MediaCaptureSettings()));
                return selectedFile;
            }
            catch (Exception ex)
            {
                await errorAction.Apply(ex, "Failed to take a photo: " + ex.Message);
                return null;
            }
        }

        /// <summary>Saves a taken video into a local temp folder in the device's cache folder and returns it.</summary>
        public async Task<FileInfo> TakeVideo(Device.MediaCaptureSettings settings = null, OnError errorAction = OnError.Alert)
        {
            if (!await IsCameraAvailable())
            {
                await errorAction.Apply("No available camera was found on this device.");
                return null;
            }

            if (!SupportsTakingVideo())
            {
                await errorAction.Apply("Your device does not support recoding video.");
                return null;
            }

            if (!await Device.Permission.Camera.IsRequestGranted())
            {
                await SuggestLaunchingSettings(errorAction, "Permission was denied to access the camera.");
                return null;
            }

            try
            {
                selectedFile = await Thread.UI.Run(() => DoTakeVideo(settings ?? new Device.MediaCaptureSettings()));
                return selectedFile;
            }
            catch (Exception ex)
            {
                await errorAction.Apply(ex, "Failed to capture a video: " + ex.Message);
                return null;
            }
        }

        /// <summary>Saves a picked photo into a local temp folder in the device's cache folder and returns it.</summary>
        public async Task<FileInfo> PickPhoto(OnError errorAction = OnError.Alert)
        {
            if (!SupportsPickingPhoto())
            {
                await errorAction.Apply("Your device does not support picking photos.");
                return null;
            }

            if (!await Device.Permission.Albums.IsRequestGranted())
            {
                await SuggestLaunchingSettings(errorAction, "Permission was denied to access the device gallery.");
                return null;
            }

            try
            {
                selectedFile = await Thread.UI.Run(DoPickPhoto);
                return selectedFile;
            }
            catch (Exception ex)
            {
                await errorAction.Apply("Failed to pick a photo: " + ex.Message);
                return null;
            }
        }

        /// <summary>Saves a picked video into a local temp folder in the device's cache folder and returns it.</summary>
        public async Task<FileInfo> PickVideo(OnError errorAction = OnError.Alert)
        {
            if (!SupportsPickingVideo())
            {
                await Alert.Show("Your device does not support picking videos.");
                return null;
            }

            if (!await Device.Permission.Albums.IsRequestGranted())
            {
                await SuggestLaunchingSettings(errorAction, "Permission was denied to access the device gallery.");
                return null;
            }

            try
            {
                selectedFile = await Thread.UI.Run(DoPickVideo);
                return selectedFile;
            }
            catch (Exception ex)
            {
                await errorAction.Apply("Failed to pick a video: " + ex.Message);
                return null;
            }
        }

        async static Task SuggestLaunchingSettings(OnError errorAction, string error)
        {
            if (errorAction == OnError.Ignore || errorAction == OnError.Throw)
            {
                await errorAction.Apply(error);
            }
            else
            {
                var launchSettings = await Alert.Confirm(error + " Do you want to go to your device settings to enable it?");
                if (launchSettings) await Device.OS.OpenSettings();
            }
        }

        /// <summary>
        /// Saves a specified image file to the device's camera roll.
        /// </summary>
        public async Task<bool> SaveToAlbum(FileInfo file, OnError errorAction = OnError.Alert)
        {
            try
            {
                if (!await Device.Permission.Albums.IsRequestGranted())
                    throw new Exception("Permission to access the device albums (gallery) was denied.");

                await Thread.UI.Run(() => DoSaveToAlbum(file));
                return true;
            }
            catch (Exception ex)
            {
                await errorAction.Apply(ex, "Failed to save a file to album: " + ex.Message);
                return false;
            }
        }
    }
}