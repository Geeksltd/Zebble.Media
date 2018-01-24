namespace Zebble.Device
{
    using UIKit;

    partial class Media
    {
        internal class PickerPopoverDelegate : UIPopoverControllerDelegate
        {
            PickerDelegate Delegate;
            UIImagePickerController Picker;

            internal PickerPopoverDelegate(PickerDelegate pickerDelegate, UIImagePickerController picker)
            {
                Delegate = pickerDelegate;
                Picker = picker;
            }

            public override bool ShouldDismiss(UIPopoverController popoverController) => true;

            public override void DidDismiss(UIPopoverController popoverController) => Delegate.Canceled(Picker);
        }
    }
}
