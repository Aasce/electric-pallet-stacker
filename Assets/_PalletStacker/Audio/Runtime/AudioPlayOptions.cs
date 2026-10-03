using UnityEngine.Audio;

namespace ElectricPalletStackers.Audio
{
    /// <summary>
    /// Per-play overrides. Null values keep the defaults stored in the audio library.
    /// </summary>
    public struct AudioPlayOptions
    {
        public bool? Loop { get; set; }
        public float? Volume { get; set; }
        public float? Pitch { get; set; }
        public float? SpatialBlend { get; set; }
        public AudioMixerGroup Output { get; set; }
        public bool OverrideOutput { get; set; }

        public static AudioPlayOptions WithOutput(AudioMixerGroup output)
        {
            return new AudioPlayOptions
            {
                Output = output,
                OverrideOutput = true
            };
        }
    }
}
