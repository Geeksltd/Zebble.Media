namespace Zebble.Device
{
    using Android.App;
    using Android.Content;
    using Android.Content.PM;
    using Android.Database;
    using Android.OS;
    using Android.Provider;
    using AndroidX.Core.Content;
    using System;
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;
    using System.Collections.Generic;
    using Uri = Android.Net.Uri;
    using Olive;
    using Android.Graphics;

    partial class Media
    {
        [Activity(ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize)]
        [Android.Runtime.Preserve(AllMembers = true)]
        public class PickerActivity : Activity, Android.Media.MediaScannerConnection.IOnScanCompletedListener
        {
            internal static readonly AsyncEvent<MediaPickedEventArgs> Picked = new AsyncEvent<MediaPickedEventArgs>();

            int RequestId;
            bool FrontCamera, IsVideo;
            string Type, Action;
            bool AllowMultiple, PurgeCameraRoll;
            int MaxSeconds;
            VideoQuality VideoQuality;
            Uri FilePath;

            protected override void OnSaveInstanceState(Bundle outState)
            {
                outState.PutInt("id", RequestId);
                outState.PutString("type", Type);
                outState.PutString("action", Action);
                outState.PutBoolean(Intent.ExtraAllowMultiple, AllowMultiple);
                outState.PutBoolean("purge-camera-roll", PurgeCameraRoll);
                outState.PutInt(MediaStore.ExtraDurationLimit, MaxSeconds);
                outState.PutInt(MediaStore.ExtraVideoQuality, (int)VideoQuality);
                outState.PutBoolean("front", FrontCamera);

                base.OnSaveInstanceState(outState);
            }

            FileInfo PrepareTempStorageFile() => IO.CreateTempFile(IsVideo ? ".mp4" : ".jpg");

            protected override void OnCreate(Bundle savedInstanceState)
            {
                base.OnCreate(savedInstanceState);

                var bundle = savedInstanceState ?? Intent.Extras;

                RequestId = bundle.GetInt("id");
                Action = bundle.GetString("action");
                AllowMultiple = bundle.GetBoolean(Intent.ExtraAllowMultiple);
                PurgeCameraRoll = bundle.GetBoolean("purge-camera-roll");
                Type = bundle.GetString("type");
                FrontCamera = bundle.GetBoolean("front");
                if (Type == "video/*") IsVideo = true;

                using (var intent = new Intent(Action))
                {
                    try
                    {
                        if (Action == Intent.ActionPick)
                        {
                            intent.SetType(Type);
                            intent.PutExtra(Intent.ExtraAllowMultiple, AllowMultiple);
                        }
                        else
                        {
                            if (IsVideo)
                            {
                                MaxSeconds = bundle.GetInt(MediaStore.ExtraDurationLimit, 0);
                                if (MaxSeconds != 0) intent.PutExtra(MediaStore.ExtraDurationLimit, MaxSeconds);

                                VideoQuality = (VideoQuality)bundle.GetInt(MediaStore.ExtraVideoQuality, (int)VideoQuality.High);
                                intent.PutExtra(MediaStore.ExtraVideoQuality, VideoQuality == VideoQuality.Low ? 0 : 1);
                            }

                            if (FrontCamera)
                                intent.PutExtra("android.intent.extras.CAMERA_FACING", 1);

                            var file = new Java.IO.File(PrepareTempStorageFile().FullName);
                            Uri path;
                            var packageName = UIRuntime.CurrentActivity.PackageName;
                            if (Build.VERSION.SdkInt >= BuildVersionCodes.N)
                                path = FileProvider.GetUriForFile(Application.Context, $"{packageName}.fileprovider", file);
                            else path = Uri.FromFile(file);

                            intent.PutExtra(MediaStore.ExtraOutput, path.ToString());
                            FilePath = path;
                        }


                        // if (intent.ResolveActivity(PackageManager) != null)
                        // Removed due to Android 11 changes.
                        // https://cketti.de/2020/09/03/avoid-intent-resolveactivity/
                        // https://stackoverflow.com/questions/62535856/intent-resolveactivity-returns-null-in-api-30
                        StartActivityForResult(intent, RequestId);
                    }
                    catch (Exception ex)
                    {
                        Log.For(this).Error(ex);
                        Picked.RaiseOn(Thread.Pool, new MediaPickedEventArgs(RequestId, ex));
                        Finish();
                    }
                }
            }

            protected override async void OnActivityResult(int requestCode, Result resultCode, Intent data)
            {
                base.OnActivityResult(requestCode, resultCode, data);

                if (requestCode != RequestId)
                    return;

                if (resultCode == Result.Canceled)
                {
                    Finish();
                    await Task.Delay(50);
                    await Picked.RaiseOn(Thread.Pool, new MediaPickedEventArgs(requestCode, default(FileInfo[])));
                }
                else
                {
                    var results = new List<FileInfo>();
                    if (AllowMultiple && data?.ClipData?.ItemCount > 0)
                    {
                        for (int i = 0; i < data?.ClipData.ItemCount; i++)
                            results.Add(SaveResult(data?.ClipData.GetItemAt(i).Uri));
                    }
                    else if (data.Data == null)
                    {
                        var bitmap = (Bitmap)data.Extras.Get("data");
                        results.Add(SaveResult(bitmap));
                    }
                    else results.Add(SaveResult(data?.Data));

                    var media = results.ExceptNull().ToArray();

                    Finish();
                    await Task.Delay(50);
                    await Picked.RaiseOn(Thread.Pool, new MediaPickedEventArgs(RequestId, media));
                }
            }

            FileInfo SaveResult(Uri uri)
            {
                var result = IO.CreateTempDirectory(globalCache: false).GetFile($"File.{ShortGuid.NewGuid()}." + "mp4".OnlyWhen(IsVideo).Or("jpg"));

                var fileUri = uri ?? FilePath;

                if (fileUri?.Scheme == "file")
                {
                    var file = new System.Uri(fileUri.ToString()).LocalPath;
                    if (file != result.FullName) File.Copy(file, result.FullName);
                }
                else if (fileUri?.Scheme == "content") SaveContentToFile(fileUri, result);

                try { if (PurgeCameraRoll) ContentResolver.Delete(fileUri, null, null); }
                catch { }

                return result;
            }

            FileInfo SaveResult(Bitmap bitmap)
            {
                var result = IO.CreateTempDirectory(globalCache: false).GetFile($"File.{ShortGuid.NewGuid()}.png");
                using (var output = new FileStream(result.FullName, FileMode.Create))
                    bitmap.Compress(Bitmap.CompressFormat.Png, 100, output);

                return result;
            }

            void SaveContentToFile(Uri uri, FileInfo result)
            {
                ICursor cursor = null;
                try
                {
                    string[] proj = null;
                    if (Device.OS.IsAtLeast(BuildVersionCodes.LollipopMr1))
                        proj = new[] { MediaStore.MediaColumns.Data };

                    cursor = ContentResolver.Query(uri, proj, null, null, null);
                    if (cursor == null || !cursor.MoveToNext())
                    {
                        result = null;
                    }
                    else
                    {
                        var column = cursor.GetColumnIndex(MediaStore.MediaColumns.Data);
                        string contentPath = null;

                        if (column != -1) contentPath = cursor.GetString(column);

                        if (contentPath?.StartsWith("file", caseSensitive: false) == true)
                        {
                            File.Copy(contentPath, result.FullName);
                        }
                        else
                        {
                            try
                            {
                                using (var input = ContentResolver.OpenInputStream(uri))
                                using (var output = File.Create(result.FullName))
                                    input.CopyTo(output);
                            }
                            catch (Exception ex)
                            {
                                result = null;
                                Log.For(this).Error(ex, "Failed to save the picked file.");
                            }
                        }
                    }
                }
                finally
                {
                    cursor?.Close();
                    cursor?.Dispose();
                }
            }

            void Android.Media.MediaScannerConnection.IOnScanCompletedListener.OnScanCompleted(string _, Uri __) { }
        }
    }
}