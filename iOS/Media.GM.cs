namespace Zebble.Device
{
    using Foundation;
    using System;
    using System.IO;
    using System.Threading.Tasks;
    using System.Collections.Generic;
    using UIKit;
    using Olive;
    using GMImagePicker;
    using Photos;
    using AVFoundation;

    partial class Media
    {
        //static Task<FileInfo[]> DoPickPhoto(bool enableMultipleSelection)
        //{
        //    return LaunchGMMediaPicker(PHAssetMediaType.Image, enableMultipleSelection, new MediaCaptureSettings());
        //}

        //static Task<FileInfo[]> DoPickVideo(bool enableMultipleSelection)
        //{
        //    return LaunchGMMediaPicker(PHAssetMediaType.Video, enableMultipleSelection, new MediaCaptureSettings());
        //}

        static Task<FileInfo[]> LaunchGMMediaPicker(PHAssetMediaType mediaType, bool enableMultipleSelection, MediaCaptureSettings settings)
        {
            Log.For(typeof(Media)).Warning("LaunchMediaPicker called");
            return Thread.UI.Run(() =>
            {
                try
                {
                    return DoLaunchGMMediaPicker(mediaType, enableMultipleSelection, settings);
                }
                catch (Exception ex)
                {
                    Log.For(typeof(Media)).Error(ex);
                    return Task.FromResult<FileInfo[]>(null);
                }
            });
        }

        static async Task<FileInfo[]> DoLaunchGMMediaPicker(PHAssetMediaType mediaType, bool enableMultipleSelection, MediaCaptureSettings settings)
        {
            Log.For(typeof(Media)).Warning("DoLaunchMediaPicker called");
            var controller = UIRuntime.Window.RootViewController;

            while (controller.PresentedViewController != null)
                controller = controller.PresentedViewController;

            var picker = CreateGMController(mediaType, enableMultipleSelection, settings);
            var source = new TaskCompletionSource<FileInfo[]>();

            async void PickerOnFinishedPickingAssets(object sender, MultiAssetEventArgs args)
            {
                picker.FinishedPickingAssets -= PickerOnFinishedPickingAssets;

                var result = new List<FileInfo>();
                foreach (var asset in args.Assets)
                    result.Add(await SaveAsset(asset));

                source.SetResult(result.ToArray());
            }

            void PickerOnCanceled(object sender, EventArgs e)
            {
                picker.Canceled -= PickerOnCanceled;

                source.SetResult(null);
            }

            picker.FinishedPickingAssets += PickerOnFinishedPickingAssets;
            picker.Canceled += PickerOnCanceled;

            var usePopup = UIDevice.CurrentDevice.UserInterfaceIdiom == UIUserInterfaceIdiom.Pad;

            if (usePopup)
                picker.ModalPresentationStyle = UIModalPresentationStyle.Popover;
            else if (OS.IsAtLeastiOS(9))
                picker.ModalPresentationStyle = UIModalPresentationStyle.OverCurrentContext;

            controller.PresentViewController(picker, animated: true, completionHandler: null);

            return await source.Task;
        }

        static Task<FileInfo> SaveAsset(PHAsset asset)
        {
            var source = new TaskCompletionSource<FileInfo>();

            void CopyFile(string path)
            {
                FileInfo result;
                var ext = Path.GetExtension(path);
                if (asset.MediaType == PHAssetMediaType.Image && ext.ToLower().Contains("heic"))
                {
                    result = IO.CreateTempDirectory(globalCache: false).GetFile("File.jpg");

                    var sourceFile = File.ReadAllBytes(path);
                    var jpgData = new UIImage(NSData.FromArray(sourceFile)).AsJPEG().ToArray();
                    result.WriteAllBytes(jpgData);
                }
                else
                {
                    result = IO.CreateTempDirectory(globalCache: false).GetFile("File" + ext);
                    File.Copy(path, result.FullName);
                }

                source.SetResult(result);
            }

            void SaveFile(byte[] data)
            {
                var jpgFile = IO.CreateTempDirectory(globalCache: false).GetFile("File.jpg");
                jpgFile.WriteAllBytes(data);

                source.SetResult(jpgFile);
            }

            switch (asset.MediaType)
            {
                case PHAssetMediaType.Image: FindImagePath(asset, CopyFile, SaveFile); break;
                case PHAssetMediaType.Video: FindVideoPath(asset, CopyFile); break;
                default: throw new NotSupportedException($"Saving {asset.MediaType} not supported.");
            };

            return source.Task;
        }

        static void FindImagePath(PHAsset asset, Action<string> onPathDetermined, Action<byte[]> OnImageDetermined)
        {
            PHImageManager.DefaultManager.RequestImageData(asset, null, (data, dataUti, orientation, info) =>
            {
                var path = (info?[(NSString)@"PHImageFileURLKey"] as NSUrl)?.FilePathUrl.Path;

                if (path.HasValue())
                    onPathDetermined(path);
                else asset.RequestContentEditingInput(null, (contentEditingInput, requestStatusInfo) =>
                {
                    using (contentEditingInput)
                    {
                        if (contentEditingInput?.FullSizeImageUrl?.FilePathUrl.Path.HasValue() == true)
                            onPathDetermined(contentEditingInput.FullSizeImageUrl.FilePathUrl.Path);
                        else
                            OnImageDetermined(new UIImage(data).AsJPEG().ToArray());
                    }
                });
            });
        }

        static void FindVideoPath(PHAsset asset, Action<string> onPathDetermined)
        {
            PHImageManager.DefaultManager.RequestAvAsset(asset, null, (avAsset, audioMix, info) =>
            {
                var path = (avAsset as AVUrlAsset)?.Url.Path;

                if (path.HasValue())
                    onPathDetermined(path);
                else
                    throw new Exception("Couldn't determine the video path!");
            });
        }

        static GMImagePickerController CreateGMController(PHAssetMediaType mediaType, bool enableMultipleSelection, MediaCaptureSettings settings)
        {
            return new GMImagePickerController
            {
                MediaTypes = new[] { mediaType },
                AllowsMultipleSelection = enableMultipleSelection,
                AllowsEditingCameraImages = settings.AllowEditing,
                ShowCameraButton = false
            };
        }
    }
}