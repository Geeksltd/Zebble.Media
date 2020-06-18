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
    using System.Threading.Tasks;
    using Uri = Android.Net.Uri;

    partial class Media
    {
        [Activity(ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize)]
        [Android.Runtime.Preserve(AllMembers = true)]
        public class PickerActivity : Activity, Android.Media.MediaScannerConnection.IOnScanCompletedListener
        {
            internal static readonly AsyncEvent<MediaPickedEventArgs> Picked = new AsyncEvent<MediaPickedEventArgs>();

            FileInfo Result;
            int RequestId;
            bool FrontCamera, IsVideo;
            string Type, Action;
            int MaxSeconds;
            VideoQuality VideoQuality;
            Java.IO.File TempStorageFile;
            Uri FilePath;

            protected override void OnSaveInstanceState(Bundle outState)
            {
                outState.PutInt("id", RequestId);
                outState.PutString("type", Type);
                outState.PutString("action", Action);
                outState.PutInt(MediaStore.ExtraDurationLimit, MaxSeconds);
                outState.PutInt(MediaStore.ExtraVideoQuality, (int)VideoQuality);
                outState.PutBoolean("front", FrontCamera);

                base.OnSaveInstanceState(outState);
            }

            FileInfo PrepareTempStorageFile() => IO.CreateTempFile(IsVideo ? ".mp4" : "jpg");

            protected override void OnCreate(Bundle savedInstanceState)
            {
                base.OnCreate(savedInstanceState);

                var bundle = savedInstanceState ?? Intent.Extras;

                RequestId = bundle.GetInt("id");
                Action = bundle.GetString("action");
                Type = bundle.GetString("type");
                FrontCamera = bundle.GetBoolean("front");
                if (Type == "video/*") IsVideo = true;

                Result = Device.IO.CreateTempDirectory(globalCache: false).GetFile("File." + "mp4".OnlyWhen(IsVideo).Or("jpg"));

                using (var intent = new Intent(Action))
                {
                    try
                    {
                        if (Action == Intent.ActionPick) intent.SetType(Type);
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

                            intent.PutExtra(MediaStore.ExtraOutput, path);
                            FilePath = path;
                        }

                        if (intent.ResolveActivity(PackageManager) != null)
                            StartActivityForResult(intent, RequestId);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex);
                        Picked.RaiseOn(Thread.Pool, new MediaPickedEventArgs(RequestId, ex));
                        Finish();
                    }
                }
            }

            protected override async void OnActivityResult(int requestCode, Result resultCode, Intent data)
            {
                base.OnActivityResult(requestCode, resultCode, data);

                if (resultCode == Android.App.Result.Canceled)
                {
                    Finish();
                    await Task.Delay(50);
                    await Picked.RaiseOn(Thread.Pool, new MediaPickedEventArgs(requestCode, default(FileInfo)));
                }
                else
                {
                    SaveResult(data?.Data);

                    Finish();
                    await Task.Delay(50);
                    await Picked.RaiseOn(Thread.Pool, new MediaPickedEventArgs(RequestId, Result));
                }
            }

            void SaveResult(Uri uri)
            {
                var fileUri = uri ?? FilePath;
                if (TempStorageFile?.Exists() == true)
                    File.Move(TempStorageFile.ToString(), Result.FullName);
                else if (fileUri?.Scheme == "file")
                {
                    var file = new System.Uri(fileUri.ToString()).LocalPath;
                    if (file != Result.FullName) File.Copy(file, Result.FullName);
                }
                else if (fileUri?.Scheme == "content") SaveContentToFile(fileUri);
            }

            void SaveContentToFile(Uri uri)
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
                        Result = null;
                    }
                    else
                    {
                        var column = cursor.GetColumnIndex(MediaStore.MediaColumns.Data);
                        string contentPath = null;

                        if (column != -1) contentPath = cursor.GetString(column);

                        if (contentPath?.StartsWith("file", caseSensitive: false) == true)
                        {
                            File.Copy(contentPath, Result.FullName);
                        }
                        else
                        {
                            try
                            {
                                using (var input = ContentResolver.OpenInputStream(uri))
                                using (var output = File.Create(Result.FullName))
                                    input.CopyTo(output);
                            }
                            catch
                            {
                                Result = null;
                                Device.Log.Error("Failed to save the picked file.");
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