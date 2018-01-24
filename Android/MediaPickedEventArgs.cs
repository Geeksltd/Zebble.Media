namespace Zebble.Device
{
    using System;
    using System.IO;

    internal class MediaPickedEventArgs
        : EventArgs
    {
        public MediaPickedEventArgs(int id, Exception error) { RequestId = id; Error = error; }

        public MediaPickedEventArgs(int id, FileInfo media = null)
        {
            RequestId = id;
            Media = media;
        }

        public int RequestId { get; }

        public Exception Error { get; }

        public FileInfo Media { get; }
    }
}