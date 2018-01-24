namespace Zebble.Device
{
    using System;
    using System.IO;
    using System.Threading.Tasks;
    using Android.Content;
    using Android.Content.PM;
    using Android.Media;
    using Android.Provider;
    using Zebble.Services;

    partial class Media
    {
        static int NextRequestId;

        public static Task<bool> IsCameraAvailable()
        {
            var packageManager = Android.App.Application.Context.PackageManager;

            if (packageManager.HasSystemFeature(PackageManager.FeatureCamera))
                return Task.FromResult(result: true);

            if (packageManager.HasSystemFeature(PackageManager.FeatureCameraFront))
                return Task.FromResult(result: true);

            return Task.FromResult(result: false);
        }

        public static bool SupportsTakingPhoto() => true;

        public static bool SupportsPickingPhoto() => true;

        public static bool SupportsTakingVideo() => true;

        public static bool SupportsPickingVideo() => true;

        static async Task<FileInfo> DoTakePhoto(Device.MediaCaptureSettings settings)
        {
            var result = await TakeMedia("image/*", MediaStore.ActionImageCapture, settings);
            await FixOrientation(result);
            return result;
        }

        static Task<FileInfo> DoTakeVideo(Device.MediaCaptureSettings settings)
        {
            return TakeMedia("video/*", MediaStore.ActionVideoCapture, settings);
        }

        static async Task<FileInfo> DoPickPhoto()
        {
            var result = await TakeMedia("image/*", Intent.ActionPick, new Device.MediaCaptureSettings());
            await FixOrientation(result);
            return result;
        }

        static Task<FileInfo> DoPickVideo()
        {
            return TakeMedia("video/*", Intent.ActionPick, new Device.MediaCaptureSettings());
        }

        static Task<FileInfo> TakeMedia(string type, string action, Device.MediaCaptureSettings options)
        {
            var id = NextRequestId++;

            var completionSource = new TaskCompletionSource<FileInfo>(id);

            UIRuntime.CurrentActivity.StartActivity(CreateIntent(id, type, action, options));

            void handler(MediaPickedEventArgs e)
            {
                GC.Collect();
                PickerActivity.Picked.RemoveHandler((Action<MediaPickedEventArgs>)handler);

                if (e.RequestId != id) return;

                if (!e.Media.Exists()) completionSource.TrySetResult(null);
                else if (e.Error != null) completionSource.SetException(e.Error);
                else completionSource.TrySetResult(e.Media);
            }

            PickerActivity.Picked.Handle((Action<MediaPickedEventArgs>)handler);
            GC.Collect();
            return completionSource.Task;
        }

        static Intent CreateIntent(int id, string type, string action, Device.MediaCaptureSettings settings)
        {
            var result = new Intent(UIRuntime.CurrentActivity, typeof(PickerActivity))
            .PutExtra("id", id)
            .PutExtra("type", type)
            .PutExtra("action", action)
            .SetFlags(ActivityFlags.NewTask);

            if (settings.Camera == CameraOption.Front)
                result.PutExtra("android.intent.extras.CAMERA_FACING", 1);

            if (action == MediaStore.ActionVideoCapture)
            {
                if (settings.VideoMaxDuration.HasValue)
                    result.PutExtra(MediaStore.ExtraDurationLimit, (int)settings.VideoMaxDuration.Value.TotalSeconds);

                if (settings.VideoQuality == VideoQuality.Low) result.PutExtra(MediaStore.ExtraVideoQuality, 0);
                else result.PutExtra(MediaStore.ExtraVideoQuality, 1);
            }

            return result;
        }

        static async Task FixOrientation(FileInfo file)
        {
            if (file != null)
                await ImageService.Rotate(file, file, ImageService.FindExifRotationDegrees(file));
        }

        static Task DoSaveToAlbum(FileInfo file)
        {
            var isPhoto = file.GetMimeType().StartsWith("image");

            var mediaType = isPhoto ? Android.OS.Environment.DirectoryPictures : Android.OS.Environment.DirectoryMovies;
            var directory = new DirectoryInfo(Android.OS.Environment.GetExternalStoragePublicDirectory(mediaType).Path)
             .GetOrCreateSubDirectory(Device.IO.Root.Name);

            if (!directory.Exists()) throw new IOException("Failed to create directory " + directory.Name);

            var destination = directory.GetFile(file.Name);

            if (destination.Exists())
            {
                var num = 1;
                while (true)
                {
                    destination = directory.GetFile(file.NameWithoutExtension() + " " + num + file.Extension);
                    if (!destination.Exists()) break;
                    num++;
                }
            }

            file.CopyTo(destination);

            try
            {
                MediaScannerConnection.ScanFile(UIRuntime.CurrentActivity, new[] { destination.FullName }, null, null);

                var values = new ContentValues();
                values.Put(MediaStore.Images.Media.InterfaceConsts.Title, destination.NameWithoutExtension());
                values.Put(MediaStore.Images.Media.InterfaceConsts.Description, string.Empty);
                values.Put(MediaStore.Images.Media.InterfaceConsts.DateTaken, Java.Lang.JavaSystem.CurrentTimeMillis());
                values.Put(MediaStore.Images.ImageColumns.BucketId, destination.FullName.GetHashCode());
                values.Put(MediaStore.Images.ImageColumns.BucketDisplayName, destination.Name.ToLowerInvariant());
                values.Put("_data", destination.FullName);

                UIRuntime.CurrentActivity.ContentResolver.Insert(MediaStore.Images.Media.ExternalContentUri, values);
            }
            catch (Exception ex)
            {
                Device.Log.Error("Failed to save to scan file: " + ex);
            }

            var publicUri = Android.Net.Uri.FromFile(new Java.IO.File(destination.FullName));
            var mediaScanIntent = new Intent(Intent.ActionMediaScannerScanFile, publicUri);
            UIRuntime.CurrentActivity.SendBroadcast(mediaScanIntent);

            return Task.CompletedTask;
        }
    }
}