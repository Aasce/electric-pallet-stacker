namespace ElectricPalletStackers.Audio
{
    /// <summary>
    /// Safe reference to a pooled voice. A stale handle cannot affect a reused AudioSource.
    /// </summary>
    public readonly struct AudioHandle
    {
        private readonly AudioManager _owner;
        private readonly int _voiceId;
        private readonly uint _generation;

        internal AudioHandle(AudioManager owner, int voiceId, uint generation)
        {
            _owner = owner;
            _voiceId = voiceId;
            _generation = generation;
        }

        public bool IsValid => _owner != null && _owner.IsHandleValid(_voiceId, _generation);
        public bool IsPlaying => _owner != null && _owner.IsPlaying(_voiceId, _generation);

        public void Stop()
        {
            if (_owner != null) _owner.Stop(_voiceId, _generation);
        }

        public void SetVolume(float volume)
        {
            if (_owner != null) _owner.SetVolume(_voiceId, _generation, volume);
        }
    }
}
