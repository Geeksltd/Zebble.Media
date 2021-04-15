namespace Zebble.Device
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;

    public static partial class Media
    {
        /// <summary>Saves a picked photo file into a local temp folder in the device's cache folder and returns it.</summary>
        public static async Task<FileInfo> TakePhoto(MediaCaptureSettings settings = null, OnError errorAction = OnError.Alert)
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

            if (!await Permission.Camera.IsRequestGranted())
            {
                await SuggestLaunchingSettings(errorAction, "Permission was denied to access the camera.");
                return null;
            }

#if ANDROID
            if (settings?.PurgeCameraRoll == true)
            {
                if (!await Device.Permission.ExternalStorage.IsRequestGranted()) {
                    await SuggestLaunchingSettings(errorAction, "Permission was denied to access the external storage.");
                    return null;
                }
            }
#endif

            try
            {
                return await Thread.UI.Run(() => DoTakePhoto(settings ?? new Device.MediaCaptureSettings()));
            }
            catch (Exception ex)
            {
                await errorAction.Apply(ex, "Failed to take a photo: " + ex.Message);
                return null;
            }
        }

        /// <summary>Saves a taken video into a local temp folder in the device's cache folder and returns it.</summary>
        public static async Task<FileInfo> TakeVideo(MediaCaptureSettings settings = null, OnError errorAction = OnError.Alert)
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

            if (!await Permission.Camera.IsRequestGranted())
            {
                await SuggestLaunchingSettings(errorAction, "Permission was denied to access the camera.");
                return null;
            }

#if ANDROID
            if (settings?.PurgeCameraRoll == true)
            {
                if (!await Permission.ExternalStorage.IsRequestGranted()) {
                    await SuggestLaunchingSettings(errorAction, "Permission was denied to access the external storage.");
                    return null;
                }
            }
#endif

            try
            {
                return await Thread.UI.Run(() => DoTakeVideo(settings ?? new MediaCaptureSettings()));
            }
            catch (Exception ex)
            {
                await errorAction.Apply(ex, "Failed to capture a video: " + ex.Message);
                return null;
            }
        }

        /// <summary>Saves a picked photo into a local temp folder in the device's cache folder and returns it.</summary>
        public static async Task<FileInfo> PickPhoto(OnError errorAction = OnError.Alert)
        {
            return (await PickPhotoCore(enableMultipleSelection: false, errorAction).ConfigureAwait(false)).FirstOrDefault();
        }

        /// <summary>Saves a set of picked photos into a local temp folder in the device's cache folder and returns them.</summary>
        public static Task<FileInfo[]> PickMultiplePhotos(OnError errorAction = OnError.Alert)
        {
            return PickPhotoCore(enableMultipleSelection: true, errorAction);
        }

        static async Task<FileInfo[]> PickPhotoCore(bool enableMultipleSelection, OnError errorAction = OnError.Alert)
        {
            if (!SupportsPickingPhoto())
            {
                await errorAction.Apply("Your device does not support picking photos.");
                return null;
            }

            if (!await Permission.Albums.IsRequestGranted())
            {
                await SuggestLaunchingSettings(errorAction, "Permission was denied to access the device gallery.");
                return null;
            }

            try
            {
                return await Thread.UI.Run(() => DoPickPhoto(enableMultipleSelection));
            }
            catch (Exception ex)
            {
                await errorAction.Apply("Failed to pick a photo: " + ex.Message);
                return null;
            }
        }

        /// <summary>Saves a picked video into a local temp folder in the device's cache folder and returns it.</summary>
        public static async Task<FileInfo> PickVideo(OnError errorAction = OnError.Alert)
        {
            return (await PickVideoCore(enableMultipleSelection: false, errorAction).ConfigureAwait(false)).FirstOrDefault();
        }

        /// <summary>Saves a set of picked videos into a local temp folder in the device's cache folder and returns them.</summary>
        public static Task<FileInfo[]> PickMultipleVideos(OnError errorAction = OnError.Alert)
        {
            return PickVideoCore(enableMultipleSelection: true, errorAction);
        }

        static async Task<FileInfo[]> PickVideoCore(bool enableMultipleSelection, OnError errorAction = OnError.Alert)
        {
            if (!SupportsPickingVideo())
            {
                await Alert.Show("Your device does not support picking videos.");
                return null;
            }

            if (!await Permission.Albums.IsRequestGranted())
            {
                await SuggestLaunchingSettings(errorAction, "Permission was denied to access the device gallery.");
                return null;
            }

            try
            {
                return await Thread.UI.Run(() => DoPickVideo(enableMultipleSelection)).ConfigureAwait(false);
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
                if (launchSettings) await OS.OpenSettings();
            }
        }

        /// <summary>
        /// Saves a specified image file to the device's camera roll.
        /// </summary>
        public static async Task<bool> SaveToAlbum(FileInfo file, OnError errorAction = OnError.Alert)
        {
            try
            {
                if (!await Permission.Albums.IsRequestGranted())
                    throw new Exception("Permission to access the device albums (gallery) was denied.");

                await Thread.UI.Run(() => DoSaveToAlbum(file)).ConfigureAwait(false);
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