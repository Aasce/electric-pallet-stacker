# Replacing cutscene videos

The game now loads these videos by filename instead of embedding them in a Unity `VideoClip`:

| Cutscene | Filename |
| --- | --- |
| Phone distraction accident | `phone_distraction.mp4` |
| Cross safety foot accident | `cross_safety_foot_accident.mp4` |

Use MP4 with H.264 video and AAC audio for the best compatibility with the current Windows and Android builds. Keep the filename unchanged when replacing a video. The new video takes effect after restarting the game.

## Windows build

Replace the corresponding file under:

```text
<game>_Data/StreamingAssets/Cutscenes/
```

No Unity rebuild is needed. A user override can also be placed under:

```text
%USERPROFILE%/AppData/LocalLow/spaxtek/Electric Pallet Stacker/Cutscenes/
```

The user override takes priority over the packaged `StreamingAssets` file.

## Android / Meta Quest build

Android packages `StreamingAssets` inside the APK, so those packaged defaults cannot be edited in place. On first use, the game copies the defaults into its persistent data folder. Put replacements in:

```text
/storage/emulated/0/Android/data/com.DefaultCompany.VRTemplate/files/Cutscenes/
```

For example, with ADB connected:

```text
adb shell mkdir -p /sdcard/Android/data/com.DefaultCompany.VRTemplate/files/Cutscenes
adb push phone_distraction.mp4 /sdcard/Android/data/com.DefaultCompany.VRTemplate/files/Cutscenes/
adb push cross_safety_foot_accident.mp4 /sdcard/Android/data/com.DefaultCompany.VRTemplate/files/Cutscenes/
```

The package identifier in that path must match the Android Application Identifier in Unity Player Settings. These persistent files take priority over the copied defaults, so replacing either file does not require rebuilding or reinstalling the game.
