namespace Zebble.Device
{
    using System;
    using System.IO;
    using System.Threading.Tasks;
    using Android.App;
    using Android.Content;
    using Android.Content.PM;
    using Android.Database;
    using Android.OS;
    using Android.Provider;
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

            Java.IO.File PrepareTempStorageFile()
            {
                var folder = new Java.IO.File(
     Android.OS.Environment.GetExternalStoragePublicDirectory(
         IsVideo ? Android.OS.Environment.DirectoryMovies :
       Android.OS.Environment.DirectoryPictures), StartUp.ApplicationName.Or("zebble").ToLower());
                if (!folder.Exists()) folder.Mkdirs();

                TempStorageFile = new Java.IO.File(folder, Guid.NewGuid().ToString().Remove("-").ToLower() +
                    (IsVideo ? ".mp4" : ".jpg"));

                folder.Dispose();

                return TempStorageFile;
            }

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

                            intent.PutExtra(MediaStore.ExtraOutput, Uri.FromFile(PrepareTempStorageFile()));
                        }

                        StartActivityForResult(intent, RequestId);
                    }
                    catch (Exception ex)
                    {
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
                if (TempStorageFile?.Exists() == true)
                    File.Move(TempStorageFile.ToString(), Result.FullName);
                else if (uri?.Scheme == "file")
                {
                    var file = new System.Uri(uri.ToString()).LocalPath;
                    if (file != Result.FullName) File.Copy(file, Result.FullName);
                }
                else if (uri?.Scheme == "content") SaveContentToFile(uri);
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
                                var fileName = Path.GetFileName(contentPath);
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