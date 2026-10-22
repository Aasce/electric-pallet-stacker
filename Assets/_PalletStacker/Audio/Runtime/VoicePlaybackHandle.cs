namespace ElectricPalletStackers.Audio
{
    /// <summary>
    /// Safe ownership token for the single narration channel. A stale presenter cannot
    /// stop a newer voice line started by another situation.
    /// </summary>
    public readonly struct VoicePlaybackHandle
    {
        private readonly AudioManager _owner;
        private readonly uint _generation;

        internal VoicePlaybackHandle(AudioManager owner, uint generation)
        {
            _owner = owner;
            _generation = generation;
        }

        public bool IsPlaying => _owner != null && _owner.IsVoicePlaying(_generation);

        public void Stop()
        {
            if (_owner != null) _owner.StopVoice(_generation);
        }
    }
}
