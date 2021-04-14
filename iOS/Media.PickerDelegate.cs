namespace Zebble.Device
{
    using CoreGraphics;
    using Foundation;
    using System;
    using System.IO;
    using System.Threading.Tasks;
    using UIKit;
    using Olive;

    partial class Media
    {
        internal class PickerDelegate : UIImagePickerControllerDelegate
        {
            bool IsPickerDisposed;
            UIDeviceOrientation? Orientation;
            NSObject OrientationObserver;
            UIViewController Controller;
            UIImagePickerControllerSourceType Source;
            internal TaskCompletionSource<FileInfo> CompletionSource = new TaskCompletionSource<FileInfo>();

            internal PickerDelegate(UIViewController viewController, UIImagePickerControllerSourceType sourceType)
            {
                Controller = viewController;
                Source = sourceType;

                if (Controller != null)
                {
                    IsPickerDisposed = false;
                    UIDevice.CurrentDevice.BeginGeneratingDeviceOrientationNotifications();
                    OrientationObserver = NSNotificationCenter.DefaultCenter.AddObserver(UIDevice.OrientationDidChangeNotification, DidRotate);
                }
            }

            public UIPopoverController Popover { get; set; }

            public override void FinishedPickingMedia(UIImagePickerController picker, NSDictionary info)
            {
                RemoveOrientationChangeObserverAndNotifications();

                FileInfo file;
                switch ((NSString)info[UIImagePickerController.MediaType])
                {
                    case PHOTO_TYPE: file = SavePhoto(info); break;
                    case VIDEO_TYPE: file = SaveVideo(info); break;
                    default: throw new NotSupportedException();
                }

                if (UIDevice.CurrentDevice.UserInterfaceIdiom == UIUserInterfaceIdiom.Phone)
                    UIApplication.SharedApplication.SetStatusBarStyle(StatusBarStyle, animated: false);

                Dismiss(picker, () => CompletionSource.TrySetResult(file));
            }

            public override void Canceled(UIImagePickerController picker)
            {
                RemoveOrientationChangeObserverAndNotifications();

                if (UIDevice.CurrentDevice.UserInterfaceIdiom == UIUserInterfaceIdiom.Phone)
                    UIApplication.SharedApplication.SetStatusBarStyle(StatusBarStyle, animated: false);

                Dismiss(picker, () => CompletionSource.TrySetResult(null));
            }

            internal void DisplayPopover(bool hideExisting = false)
            {
                if (Popover == null) return;

                var swidth = UIScreen.MainScreen.Bounds.Width;
                var sheight = UIScreen.MainScreen.Bounds.Height;

                nfloat width = 400;
                nfloat height = 300;

                if (Orientation == null)
                {
                    if (IsValidInterfaceOrientation(UIDevice.CurrentDevice.Orientation))
                        Orientation = UIDevice.CurrentDevice.Orientation;
                    else
                        Orientation = GetDeviceOrientation(Controller.InterfaceOrientation);
                }

                nfloat left, top;
                if (Orientation == UIDeviceOrientation.LandscapeLeft || Orientation == UIDeviceOrientation.LandscapeRight)
                {
                    top = swidth / 2 - height / 2;
                    left = sheight / 2 - width / 2;
                }
                else
                {
                    left = swidth / 2 - width / 2;
                    top = sheight / 2 - height / 2;
                }

                if (hideExisting && Popover.PopoverVisible) Popover.Dismiss(animated: false);

                Popover.PresentFromRect(new CGRect(left, top, width, height), Controller.View, 0, animated: true);
            }

            void Dismiss(UIImagePickerController picker, Action onDismiss)
            {
                if (Controller == null)
                {
                    onDismiss();
                    CompletionSource = new TaskCompletionSource<FileInfo>();
                }
                else if (Popover != null)
                {
                    Popover.Dismiss(animated: true);
                    Popover.Dispose();
                    Popover = null;

                    onDismiss();
                }
                else
                {
                    picker.DismissViewController(animated: true, completionHandler: onDismiss);
                    picker.Dispose();
                }

                IsPickerDisposed = true;
            }

            void RemoveOrientationChangeObserverAndNotifications()
            {
                if (Controller == null) return;
                if (IsPickerDisposed) return;

                UIDevice.CurrentDevice.EndGeneratingDeviceOrientationNotifications();
                NSNotificationCenter.DefaultCenter.RemoveObserver(OrientationObserver);
                OrientationObserver.Dispose();
            }

            void DidRotate(NSNotification notice)
            {
                var device = (UIDevice)notice.Object;
                if (!IsValidInterfaceOrientation(device.Orientation) || Popover == null) return;
                if (Orientation.HasValue && IsSameOrientationKind(Orientation.Value, device.Orientation)) return;

                if (!ShouldRotate(device.Orientation)) return;

                var orientation = Orientation;
                Orientation = device.Orientation;

                if (orientation == null) return;

                DisplayPopover(hideExisting: true);
            }

            bool ShouldRotate(UIDeviceOrientation orientation)
            {
                if (Device.OS.IsAtLeastiOS(6))
                {
                    if (!Controller.ShouldAutorotate()) return false;

                    var mask = UIInterfaceOrientationMask.Portrait;
                    switch (orientation)
                    {
                        case UIDeviceOrientation.LandscapeLeft: mask = UIInterfaceOrientationMask.LandscapeLeft; break;
                        case UIDeviceOrientation.LandscapeRight: mask = UIInterfaceOrientationMask.LandscapeRight; break;
                        case UIDeviceOrientation.Portrait: mask = UIInterfaceOrientationMask.Portrait; break;
                        case UIDeviceOrientation.PortraitUpsideDown: mask = UIInterfaceOrientationMask.PortraitUpsideDown; break;
                        default: return false;
                    }

                    return Controller.GetSupportedInterfaceOrientations().HasFlag(mask);
                }
                else
                {
                    var iorientation = UIInterfaceOrientation.Portrait;
                    switch (orientation)
                    {
                        case UIDeviceOrientation.LandscapeLeft: iorientation = UIInterfaceOrientation.LandscapeLeft; break;
                        case UIDeviceOrientation.LandscapeRight: iorientation = UIInterfaceOrientation.LandscapeRight; break;
                        case UIDeviceOrientation.Portrait: iorientation = UIInterfaceOrientation.Portrait; break;
                        case UIDeviceOrientation.PortraitUpsideDown: iorientation = UIInterfaceOrientation.PortraitUpsideDown; break;
                        default: return false;
                    }

                    return Controller.ShouldAutorotateToInterfaceOrientation(iorientation);
                }
            }

            UIImage FixOrientation(UIImage image)
            {
                // It's portrait.
                if (image.Orientation == UIImageOrientation.Up) return image;

                var transform = CGAffineTransform.MakeIdentity();

                switch (image.Orientation)
                {
                    case UIImageOrientation.Down:
                    case UIImageOrientation.DownMirrored:
                        transform = CGAffineTransform.Translate(transform, image.Size.Width, image.Size.Height);
                        transform = CGAffineTransform.Rotate(transform, (nfloat)Math.PI);
                        break;

                    case UIImageOrientation.Left:
                    case UIImageOrientation.LeftMirrored:
                        transform = CGAffineTransform.Translate(transform, image.Size.Width, 0);
                        transform = CGAffineTransform.Rotate(transform, (nfloat)(Math.PI / 2));
                        break;

                    case UIImageOrientation.Right:
                    case UIImageOrientation.RightMirrored:
                        transform = CGAffineTransform.Translate(transform, 0, image.Size.Height);
                        transform = CGAffineTransform.Rotate(transform, (nfloat)(-Math.PI / 2));
                        break;
                    default: break;
                }

                switch (image.Orientation)
                {
                    case UIImageOrientation.UpMirrored:
                    case UIImageOrientation.DownMirrored:
                        transform = CGAffineTransform.Translate(transform, image.Size.Width, 0);
                        transform = CGAffineTransform.Scale(transform, -1, 1);
                        break;

                    case UIImageOrientation.LeftMirrored:
                    case UIImageOrientation.RightMirrored:
                        transform = CGAffineTransform.Translate(transform, image.Size.Height, 0);
                        transform = CGAffineTransform.Scale(transform, -1, 1);
                        break;
                    default: break;
                }

                var ctx = new CGBitmapContext(IntPtr.Zero, (nint)image.Size.Width, (nint)image.Size.Height, image.CGImage.BitsPerComponent, 0,
                                                         image.CGImage.ColorSpace, image.CGImage.BitmapInfo);
                ctx.ConcatCTM(transform);
                switch (image.Orientation)
                {
                    case UIImageOrientation.Left:
                    case UIImageOrientation.LeftMirrored:
                    case UIImageOrientation.Right:
                    case UIImageOrientation.RightMirrored:
                        ctx.DrawImage(new CGRect(0, 0, image.Size.Height, image.Size.Width), image.CGImage);
                        break;

                    default:
                        ctx.DrawImage(new CGRect(0, 0, image.Size.Width, image.Size.Height), image.CGImage);
                        break;
                }

                var cgimg = ctx.ToImage();
                var img = UIImage.FromImage(cgimg);
                ctx.Dispose();
                cgimg.Dispose();
                return img;
            }

            FileInfo SavePhoto(NSDictionary info)
            {
                var image = (UIImage)info[UIImagePickerController.EditedImage]
                    ?? (UIImage)info[UIImagePickerController.OriginalImage];

                image = FixOrientation(image);

                var meta = info[UIImagePickerController.MediaMetadata] as NSDictionary;

                var result = IO.CreateTempDirectory().GetFile("File.jpg");

                image.AsJPEG(1).Save(result.FullName, atomically: true);

                return result;
            }

            FileInfo SaveVideo(NSDictionary info)
            {
                var result = IO.CreateTempDirectory().GetFile("File.mp4");

                var url = (NSUrl)info[UIImagePickerController.MediaURL];
                File.Copy(url.Path, result.FullName);

                return result;
            }

            static bool IsValidInterfaceOrientation(UIDeviceOrientation current)
            {
                return current != UIDeviceOrientation.FaceUp &&
                    current != UIDeviceOrientation.FaceDown &&
                    current != UIDeviceOrientation.Unknown;
            }

            static bool IsSameOrientationKind(UIDeviceOrientation o1, UIDeviceOrientation o2)
            {
                if (o1 == UIDeviceOrientation.FaceDown || o1 == UIDeviceOrientation.FaceUp)
                    return o2 == UIDeviceOrientation.FaceDown || o2 == UIDeviceOrientation.FaceUp;

                if (o1 == UIDeviceOrientation.LandscapeLeft || o1 == UIDeviceOrientation.LandscapeRight)
                    return o2 == UIDeviceOrientation.LandscapeLeft || o2 == UIDeviceOrientation.LandscapeRight;

                if (o1 == UIDeviceOrientation.Portrait || o1 == UIDeviceOrientation.PortraitUpsideDown)
                    return o2 == UIDeviceOrientation.Portrait || o2 == UIDeviceOrientation.PortraitUpsideDown;

                return false;
            }

            static UIDeviceOrientation GetDeviceOrientation(UIInterfaceOrientation current)
            {
                switch (current)
                {
                    case UIInterfaceOrientation.LandscapeLeft: return UIDeviceOrientation.LandscapeLeft;
                    case UIInterfaceOrientation.LandscapeRight: return UIDeviceOrientation.LandscapeRight;
                    case UIInterfaceOrientation.Portrait: return UIDeviceOrientation.Portrait;
                    case UIInterfaceOrientation.PortraitUpsideDown: return UIDeviceOrientation.PortraitUpsideDown;
                    default: throw new InvalidOperationException();
                }
            }
        }
    }
}