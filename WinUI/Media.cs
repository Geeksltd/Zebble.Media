﻿namespace Zebble.Device
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;
    using Windows.Devices.Enumeration;
    using Windows.Media.Capture;
    using Windows.Storage;
    using Windows.Storage.Pickers;
    using Olive;

    partial class Media
    {
        static string[] SupportedVideoExtensions = new[] { ".mp4", ".wmv", ".avi" };
        static string[] SupportedImageExtensions = new[] { ".jpeg", ".jpg", ".png", ".gif", ".bmp" };

        static List<string> EnabledDeviceIds = new List<string>();
        static DeviceWatcher Watcher;
        static bool IsInitialized;

        static Media()
        {
            Watcher = DeviceInformation.CreateWatcher(DeviceClass.VideoCapture);
            Watcher.Added += async (_, __) => await ReadOrSyncDevices();
            Watcher.Updated += async (_, __) => await ReadOrSyncDevices();
            Watcher.Removed += async (_, __) => await ReadOrSyncDevices();
            Watcher.Start();
        }

        static async Task ReadOrSyncDevices()
        {
            try
            {
                var info = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture).AsTask();
                EnabledDeviceIds = info.Where(x => x.IsEnabled).Select(x => x.Id).ToList();
            }
            catch (Exception ex) { Log.For(typeof(Media)).Error(ex, "Failed to detect cameras."); }
        }

        public static async Task<bool> IsCameraAvailable()
        {
            if (!IsInitialized)
            {
                await ReadOrSyncDevices();
                IsInitialized = true;
            }

            return EnabledDeviceIds.Any();
        }

        public static bool SupportsTakingPhoto() => true;

        public static bool SupportsPickingPhoto() => true;

        public static bool SupportsTakingVideo() => true;

        public static bool SupportsPickingVideo() => true;

        static async Task<FileInfo> DoTakePhoto(MediaCaptureSettings settings)
        {
            var capture = new CameraCaptureUI();
            capture.PhotoSettings.Format = CameraCaptureUIPhotoFormat.Jpeg;
            capture.PhotoSettings.MaxResolution = CameraCaptureUIMaxPhotoResolution.HighestAvailable;
            capture.PhotoSettings.AllowCropping = settings.AllowEditing;

            var result = await capture.CaptureFileAsync(CameraCaptureUIMode.Photo);

            return await result.SaveToTempFile();
        }

        static async Task<FileInfo> DoTakeVideo(MediaCaptureSettings settings)
        {
            var capture = new CameraCaptureUI();
            capture.VideoSettings.MaxResolution = ToResolution(settings.VideoQuality);
            capture.VideoSettings.AllowTrimming = settings.AllowEditing;
            capture.VideoSettings.Format = CameraCaptureUIVideoFormat.Mp4;

            if (settings.VideoMaxDuration.HasValue)
                capture.VideoSettings.MaxDurationInSeconds = (float)settings.VideoMaxDuration.Value.TotalSeconds;

            var video = await capture.CaptureFileAsync(CameraCaptureUIMode.Video);

            return await video.SaveToTempFile();
        }

        static Task<FileInfo[]> DoPickPhoto(bool enableMultipleSelection)
        {
            return DoPickAsset(PickerLocationId.PicturesLibrary, SupportedImageExtensions, enableMultipleSelection);
        }

        static Task<FileInfo[]> DoPickVideo(bool enableMultipleSelection)
        {
            return DoPickAsset(PickerLocationId.VideosLibrary, SupportedVideoExtensions, enableMultipleSelection);
        }

        static async Task<FileInfo[]> DoPickAsset(PickerLocationId location, string[] extensions, bool enableMultipleSelection)
        {
            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = location,
                ViewMode = PickerViewMode.Thumbnail
            };

            picker.FileTypeFilter.AddRange(extensions);

            if (enableMultipleSelection)
            {
                var files = await picker.PickMultipleFilesAsync().AsTask();
                var result = new List<FileInfo>();
                foreach (var item in files.ToList())
                    result.Add(await item.SaveToTempFile());
                return result.ToArray();
            }

            var picked = await picker.PickSingleFileAsync();
            return new[] { await picked.SaveToTempFile() };
        }

        static CameraCaptureUIMaxVideoResolution ToResolution(VideoQuality quality)
        {
            switch (quality)
            {
                case VideoQuality.Low: return CameraCaptureUIMaxVideoResolution.LowDefinition;
                case VideoQuality.Medium: return CameraCaptureUIMaxVideoResolution.StandardDefinition;
                default: return CameraCaptureUIMaxVideoResolution.HighestAvailable;
            }
        }

        static async Task DoSaveToAlbum(FileInfo file)
        {
            var storageFile = await KnownFolders.PicturesLibrary
                .CreateFileAsync(file.Name, CreationCollisionOption.GenerateUniqueName);

            using (var stream = await storageFile.OpenStreamForWriteAsync())
            {
                var data = file.ReadAllBytes();
                stream.Write(data, 0, data.Length - 1);
            }
        }
    }
}