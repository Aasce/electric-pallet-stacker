# Replacing cutscene videos

The game now loads these videos by filename instead of embedding them in a Unity `VideoClip`:

| Cutscene | Filename |
| --- | --- |
| Phone distraction accident | `phone_distraction.mp4` |
| Cross safety foot accident | `cross_safety_foot_accident.mp4` |

Use MP4 with H.264 video and AAC audio. Keep the filename unchanged when replacing a video. The new video takes effect after restarting the game.

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

## Loading behavior

At startup, `CutsceneVideoManager` creates video players on its own GameObject and calls `VideoPlayer.Prepare()` asynchronously. Each player writes to a separate render texture shown by the matching UI panel. The cutscene UI stays inactive until playback is requested. The video backend opens the file and prepares its first frame in the background; the code does not synchronously copy the whole MP4 into memory.
