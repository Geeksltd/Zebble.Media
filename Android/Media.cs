namespace Zebble.Device
{
    using Android.Content;
    using Android.Content.PM;
    using Android.Media;
    using Android.Provider;
    using System;
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;
    using Zebble.Services;
    using Olive;
    using AndroidX.Activity.Result;
    using static AndroidX.Activity.Result.Contract.ActivityResultContracts;
    using static AndroidX.Activity.Result.Contract.ActivityResultContracts.PickVisualMedia;
    using System.Collections.Generic;
    using Android.Runtime;
    using Android.Database;

    partial class Media
    {
        static int NextRequestId;

        static ActivityResultLauncher PhotoSingleLauncher;
        static ActivityResultLauncher PhotoMultiLauncher;

        static ActivityResultLauncher VideoSingleLauncher;
        static ActivityResultLauncher VideoMultiLauncher;

        public static void RegisterLaunchers()
        {
            var activity = (AndroidX.Activity.ComponentActivity)UIRuntime.CurrentActivity;

            (PhotoSingleLauncher, PhotoMultiLauncher) = CreateLaunchers(extension: "jpg");
            (VideoSingleLauncher, VideoMultiLauncher) = CreateLaunchers(extension: "mp4");

            (ActivityResultLauncher, ActivityResultLauncher) CreateLaunchers(string extension)
            {
                var handler = new ActivityResultHandler(extension);
                return (
                    activity.RegisterForActivityResult(new PickVisualMedia(), handler),
                    activity.RegisterForActivityResult(new PickMultipleVisualMedia(), handler)
                );
            }
        }

        public static Task<bool> IsCameraAvailable()
        {
            var packageManager = Android.App.Application.Context.PackageManager;

            if (packageManager is null)
                return Task.FromResult(result: false);

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

        static async Task<FileInfo> DoTakePhoto(MediaCaptureSettings settings)
        {
            var result = (await TakeMedia("image/*", MediaStore.ActionImageCapture, enableMultipleSelection: false, settings)).FirstOrDefault();
            await FixOrientation(result);
            return result;
        }

        static async Task<FileInfo> DoTakeVideo(MediaCaptureSettings settings)
        {
            return (await TakeMedia("video/*", MediaStore.ActionVideoCapture, enableMultipleSelection: false, settings)).FirstOrDefault();
        }

        static async Task<FileInfo[]> DoPickPhoto(bool enableMultipleSelection)
        {
            var result = await TakeMediaV2(enableMultipleSelection ? PhotoMultiLauncher : PhotoSingleLauncher, CreateRequest(ImageOnly.Instance));
            foreach (var r in result) await FixOrientation(r);
            return result;
        }

        static async Task<FileInfo[]> DoPickVideo(bool enableMultipleSelection)
        {
            return await TakeMediaV2(enableMultipleSelection ? VideoMultiLauncher : VideoSingleLauncher, CreateRequest(VideoOnly.Instance));
        }

        static TaskCompletionSource<FileInfo[]> CompletionSource;

        static async Task<FileInfo[]> TakeMediaV2(ActivityResultLauncher launcher, PickVisualMediaRequest request)
        {
            CompletionSource?.TrySetResult([]);
            CompletionSource = new TaskCompletionSource<FileInfo[]>();

            launcher.Launch(request);

            return await CompletionSource.Task;
        }

        static PickVisualMediaRequest CreateRequest(IVisualMediaType mediaType)
            => new PickVisualMediaRequest.Builder().SetMediaType(mediaType).Build();

        class ActivityResultHandler(string Extension) : Java.Lang.Object, IActivityResultCallback
        {
            public void OnActivityResult(Java.Lang.Object result)
            {
                var files = new List<FileInfo>();

                try
                {
                    if (result is Android.Net.Uri uri)
                    {
                        files.Add(ToFile(uri));
                    }
                    else
                    {
                        var uris = result.JavaCast<Java.Util.ArrayList>();
                        if (uris is null) return;

                        files.AddRange(uris.ToEnumerable<Android.Net.Uri>().Select(ToFile));
                    }
                }
                finally
                {
                    CompletionSource?.TrySetResult(files.ExceptNull().ToArray());
                }

                FileInfo ToFile(Android.Net.Uri uri)
                {
                    var result = IO.CreateTempDirectory(globalCache: false)
                        .GetFile($"File.{ShortGuid.NewGuid()}.{Extension}");

                    return uri?.Scheme switch
                    {
                        "file" => FromFile(uri.Path, result),
                        "content" => FromContent(uri, result),
                        _ => null,
                    };


                    static FileInfo FromFile(string source, FileInfo destination)
                    {
                        File.Copy(source, destination.FullName);
                        return destination;
                    }

                    static FileInfo FromContent(Android.Net.Uri source, FileInfo destination)
                    {
                        ICursor cursor = null;

                        try
                        {
                            var resolver = UIRuntime.CurrentActivity.ContentResolver;
                            cursor = resolver.Query(
                                source, projection: null, selection: null,
                                selectionArgs: null, sortOrder: null);

                            if (cursor == null || !cursor.MoveToNext())
                            {
                                return null;
                            }

                            var column = cursor.GetColumnIndex(MediaStore.MediaColumns.Data);
                            string contentPath = null;

                            if (column != -1) contentPath = cursor.GetString(column);

                            if (contentPath?.StartsWith("file", caseSensitive: false) == true)
                            {
                                return FromFile(contentPath, destination);
                            }

                            try
                            {
                                using var input = resolver.OpenInputStream(source);
                                using var output = File.Create(destination.FullName);

                                input.CopyTo(output);

                                return destination;
                            }
                            catch (Exception ex)
                            {
                                Log.For(typeof(Media)).Error(ex, "Failed to save the picked file.");
                                return null;
                            }
                        }
                        finally
                        {
                            cursor?.Close();
                            cursor?.Dispose();
                        }
                    }
                }
            }
        }

        static Task<FileInfo[]> TakeMedia(string type, string action, bool enableMultipleSelection, Device.MediaCaptureSettings options)
        {
            var id = NextRequestId++;

            var completionSource = new TaskCompletionSource<FileInfo[]>(id);

            UIRuntime.CurrentActivity.StartActivity(CreateIntent(id, type, action, enableMultipleSelection, options));

            void OnMediaPicked(MediaPickedEventArgs e)
            {
                PickerActivity.Picked.RemoveHandler(OnMediaPicked);

                if (e.RequestId != id) return;

                if (e.Media.Any(x => !x.Exists())) completionSource.TrySetResult(null);
                else if (e.Error != null) completionSource.SetException(e.Error);
                else completionSource.TrySetResult(e.Media);
            }

            PickerActivity.Picked.Handle(OnMediaPicked);

            return completionSource.Task;
        }

        static Intent CreateIntent(int id, string type, string action, bool enableMultipleSelection, Device.MediaCaptureSettings settings)
        {
            var result = new Intent(UIRuntime.CurrentActivity, typeof(PickerActivity))
            .PutExtra("id", id)
            .PutExtra("type", type)
            .PutExtra("action", action)
            .PutExtra(Intent.ExtraAllowMultiple, enableMultipleSelection)
            .PutExtra("purge-camera-roll", settings.PurgeCameraRoll)
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
                Log.For(typeof(Media)).Error(ex, "Failed to save to scan file.");
            }

            var publicUri = Android.Net.Uri.FromFile(new Java.IO.File(destination.FullName));
            var mediaScanIntent = new Intent(Intent.ActionMediaScannerScanFile, publicUri);
            UIRuntime.CurrentActivity.SendBroadcast(mediaScanIntent);

            return Task.CompletedTask;
        }
    }
}