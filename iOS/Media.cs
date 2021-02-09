namespace Zebble.Device
{
    using AssetsLibrary;
    using Foundation;
    using System;
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;
    using UIKit;
    using Olive;

    partial class Media
    {
        const string PHOTO_TYPE = "public.image", VIDEO_TYPE = "public.movie";
        internal static UIStatusBarStyle StatusBarStyle { get; set; }

        static Media()
        {
            Thread.UI.Run(() =>
            {
                try
                {
                    StatusBarStyle = UIApplication.SharedApplication.StatusBarStyle;
                }
                catch
                {

                    StatusBarStyle = UIStatusBarStyle.Default;
                }
            });
        }

        public static Task<bool> IsCameraAvailable()
        {
            return Thread.UI.Run(() => Task.FromResult(UIImagePickerController.IsSourceTypeAvailable(UIImagePickerControllerSourceType.Camera)));
        }

        public static bool SupportsTakingPhoto()
        {
            return Thread.UI.Run(() => UIImagePickerController.AvailableMediaTypes(UIImagePickerControllerSourceType.Camera)
               ?.Contains(PHOTO_TYPE) == true);
        }

        public static bool SupportsTakingVideo()
        {
            return Thread.UI.Run(() => UIImagePickerController.AvailableMediaTypes(UIImagePickerControllerSourceType.Camera)
                ?.Contains(VIDEO_TYPE) == true);
        }

        public static bool SupportsPickingPhoto() => true;

        public static bool SupportsPickingVideo() => true;

        static Task<FileInfo> DoTakePhoto(Device.MediaCaptureSettings settings)
        {
            return LaunchMediaPicker(UIImagePickerControllerSourceType.Camera, PHOTO_TYPE, settings);
        }

        static Task<FileInfo> DoTakeVideo(Device.MediaCaptureSettings settings)
        {
            return LaunchMediaPicker(UIImagePickerControllerSourceType.Camera, VIDEO_TYPE, settings);
        }

        static Task<FileInfo> LaunchMediaPicker(UIImagePickerControllerSourceType sourceType, string mediaType, Device.MediaCaptureSettings settings)
        {
            Log.For(typeof(Media)).Warning("LaunchMediaPicker called");
            return Thread.UI.Run(() => DoLaunchMediaPicker(sourceType, mediaType, settings));
        }

        static async Task<FileInfo> DoLaunchMediaPicker(UIImagePickerControllerSourceType sourceType, string mediaType, Device.MediaCaptureSettings settings)
        {
            Log.For(typeof(Media)).Warning("DoLaunchMediaPicker called");
            var controller = UIRuntime.Window.RootViewController;

            while (controller.PresentedViewController != null)
                controller = controller.PresentedViewController;

            var pickerDelegate = new PickerDelegate(controller, sourceType);

            var picker = CreateController(pickerDelegate, sourceType, mediaType, settings);

            var usePopup = UIDevice.CurrentDevice.UserInterfaceIdiom == UIUserInterfaceIdiom.Pad &&
                sourceType == UIImagePickerControllerSourceType.PhotoLibrary;

            if (usePopup)
            {
                pickerDelegate.Popover = new UIPopoverController(picker) { Delegate = new PickerPopoverDelegate(pickerDelegate, picker) };
                pickerDelegate.DisplayPopover();
            }
            else
            {
                if (Device.OS.IsAtLeastiOS(9))
                    picker.ModalPresentationStyle = UIModalPresentationStyle.OverCurrentContext;

                controller.PresentViewController(picker, animated: true, completionHandler: null);
            }

            return await pickerDelegate.CompletionSource.Task;
        }

        static UIImagePickerController CreateController(PickerDelegate mpDelegate, UIImagePickerControllerSourceType sourceType, string mediaType, Device.MediaCaptureSettings settings)
        {
            var picker = new UIImagePickerController
            {
                Delegate = mpDelegate,
                MediaTypes = new[] { mediaType },
                SourceType = sourceType
            };

            if (sourceType != UIImagePickerControllerSourceType.Camera) return picker;

            if (settings.Camera == CameraOption.Front)
                picker.CameraDevice = UIImagePickerControllerCameraDevice.Front;

            picker.AllowsEditing = settings.AllowEditing;

            if (settings.OverlayViewProvider != null)
            {
                var overlay = settings.OverlayViewProvider();
                if (overlay is UIView) picker.CameraOverlayView = overlay as UIView;
            }

            if (mediaType == VIDEO_TYPE)
            {
                if (settings.VideoMaxDuration.HasValue) picker.VideoMaximumDuration = settings.VideoMaxDuration.Value.TotalSeconds;
                picker.CameraCaptureMode = UIImagePickerControllerCameraCaptureMode.Video;

                switch (settings.VideoQuality)
                {
                    case VideoQuality.Medium: picker.VideoQuality = UIImagePickerControllerQualityType.Medium; break;
                    case VideoQuality.Low: picker.VideoQuality = UIImagePickerControllerQualityType.Low; break;
                    default: picker.VideoQuality = UIImagePickerControllerQualityType.High; break;
                }
            }

            return picker;
        }

        static async Task DoSaveToAlbum(FileInfo file)
        {
            var source = new TaskCompletionSource<bool>();
            var isPhoto = file.GetMimeType().StartsWith("image");

            if (isPhoto)
            {
                var bytes = file.ReadAllBytes();
                var image = await Services.ImageService.DecodeImage(bytes);
                image.SaveToPhotosAlbum((img, err) =>
                {
                    if (err != null) source.TrySetException(new Exception(err.Description));
                    else source.TrySetResult(result: true);

                    image.Dispose();
                });
            }
            else
            {
                ALAssetsLibrary lib = new ALAssetsLibrary();
                lib.WriteVideoToSavedPhotosAlbum(NSUrl.FromFilename(file.FullName), (video, error) =>
                {
                    if (error != null) source.TrySetException(new Exception(error.Description));
                    else source.TrySetResult(result: true);
                });
            }

            await source.Task;
        }
    }
}