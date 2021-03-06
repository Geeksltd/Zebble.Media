﻿[logo]: https://raw.githubusercontent.com/Geeksltd/Zebble.Media/master/icon.png "Zebble.Media"


## Zebble.Media

![logo]

A plugin to use camera for capturing or picking photos or videos on Zebble applications.


[![NuGet](https://img.shields.io/nuget/v/Zebble.Media.svg?label=NuGet)](https://www.nuget.org/packages/Zebble.Media/)


<br>


### Setup
* Available on NuGet: [https://www.nuget.org/packages/Zebble.Media/](https://www.nuget.org/packages/Zebble.Media/)
* Install in your platform client projects.
* Available for iOS, Android and UWP.
<br>


### Api Usage

Call `Zebble.Device.Media` from any project to gain access to APIs.

##### Check device camera availabelity:
```csharp
Device.Media.IsCameraAvailable();
```
Also, you can use `SupportsTakingPhoto` or `SupportsTakingVideo` methods to determine if taking photo or video is supported on the device.
```csharp
//Photo
Device.Media.SupportsTakingPhoto();
//Video
Device.Media.SupportsTakingVideo();
```
##### Taking media:
```csharp
//Photo
var photoFile = await Device.Media.TakePhoto(); 
//Video
var videoFile = await Device.Media.TakeVideo();
```
 After the media is taken, it is returned as a temporary file which can be processed in your application. `TakePhoto()` or `TakeVideo` methods will internally check for camera availablity and support for photo or video taking and even request permission to use the camera. If there is any problem or if the user cancels the process, it will simply return `Null`.
##### Picking media:
```csharp
//Photo
var photoFile = await Device.Media.PickPhoto(); 
//Video
var videoFile = await Device.Media.PickVideo();
```
##### Save a media to gallery:
```csharp
Device.Media.SaveToAlbum(myfile);
```
<br>

### Platform Specific Notes
Some platforms require some setting to make you able to use this plugin.

#### Android
if your target API is grater then 24 you should add these codes to the manifest file:

```xml
<application>
   ...
   <provider
    android:name="android.support.v4.content.FileProvider"
    android:authorities="${applicationId}.zebblefileprovider"
        android:exported="false"
        android:grantUriPermissions="true">
      <meta-data
          android:name="android.support.FILE_PROVIDER_PATHS"
          android:resource="@xml/file_paths"/>
    </provider>
    ...
</application>
```

Then, create new file under `res/xml/media_file_paths.xml` and add below code to it.

```xml
<?xml version="1.0" encoding="utf-8"?>
<paths xmlns:android="http://schemas.android.com/apk/res/android">
    <external-files-path name="my_images" path="Pictures" />
    <external-files-path name="my_movies" path="Movies" />
</paths>
```


### Methods
| Method       | Return Type  | Parameters                          | Android | iOS | Windows |
| :----------- | :----------- | :-----------                        | :------ | :-- | :------ |
| IsCameraAvailable         | Task<bool&gt;| -| x       | x   | x       |
| SupportsTakingPhoto         | bool| -| x       | x   | x       |
| SupportsTakingVideo         | bool| -| x       | x   | x       |
| SupportsPickingPhoto         | bool| -| x       | x   | x       |
| SupportsPickingVideo         | bool| -| x       | x   | x       |
| TakePhoto         | Task<FileInfo&gt;| settings -> MediaCaptureSettings<br> errorAction -> OnError| x       | x   | x       |
| TakeVideo         | Task<FileInfo&gt;| settings -> MediaCaptureSettings<br> errorAction -> OnError| x       | x   | x       |
| PickPhoto         | Task<FileInfo&gt;| errorAction -> OnError| x       | x   | x       |
| PickVideo         | Task<FileInfo&gt;| errorAction -> OnError| x       | x   | x       |
| SaveToAlbum         | Task<bool&gt;| file -> FileInfo<br> errorAction -> OnError| x       | x   | x       |

