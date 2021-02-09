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

    partial class Media
    {
        static Task<FileInfo[]> DoPickPhoto(bool enableMultipleSelection)
        {
            return LaunchGMMediaPicker(PHAssetMediaType.Image, enableMultipleSelection, new Device.MediaCaptureSettings());
        }

        static Task<FileInfo[]> DoPickVideo(bool enableMultipleSelection)
        {
            return LaunchGMMediaPicker(PHAssetMediaType.Video, enableMultipleSelection, new Device.MediaCaptureSettings());
        }

        static Task<FileInfo[]> LaunchGMMediaPicker(PHAssetMediaType mediaType, bool enableMultipleSelection, Device.MediaCaptureSettings settings)
        {
            Log.For(typeof(Media)).Warning("LaunchMediaPicker called");
            return Thread.UI.Run(() => DoLaunchGMMediaPicker(mediaType, enableMultipleSelection, settings));
        }

        static async Task<FileInfo[]> DoLaunchGMMediaPicker(PHAssetMediaType mediaType, bool enableMultipleSelection, Device.MediaCaptureSettings settings)
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
                source.SetResult(null);
            }

            picker.FinishedPickingAssets += PickerOnFinishedPickingAssets;
            picker.Canceled += PickerOnCanceled;

            var usePopup = UIDevice.CurrentDevice.UserInterfaceIdiom == UIUserInterfaceIdiom.Pad;

            if (usePopup)
                picker.ModalPresentationStyle = UIModalPresentationStyle.Popover;
            else if (Device.OS.IsAtLeastiOS(9))
                picker.ModalPresentationStyle = UIModalPresentationStyle.OverCurrentContext;

            controller.PresentViewController(picker, animated: true, completionHandler: null);

            return await source.Task;
        }

        static Task<FileInfo> SaveAsset(PHAsset asset)
        {
            var source = new TaskCompletionSource<FileInfo>();
            var fileName = (NSString)asset.ValueForKey((NSString)"filename");

            PHImageManager.DefaultManager.RequestImageData(asset, null, (data, dataUti, orientation, info) =>
            {
                var path = (info?[(NSString)@"PHImageFileURLKey"] as NSUrl).FilePathUrl.Path;
                var result = IO.CreateTempDirectory(globalCache: false).GetFile("File" + Path.GetExtension(path));

                File.Copy(path, result.FullName);

                source.SetResult(result);
            });

            return source.Task;
        }

        static GMImagePickerController CreateGMController(PHAssetMediaType mediaType, bool enableMultipleSelection, Device.MediaCaptureSettings settings)
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