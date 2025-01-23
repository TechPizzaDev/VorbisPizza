namespace NVorbis
{
    internal abstract class FloorData
    {
        public abstract bool ExecuteChannel { get; }

        public abstract void Reset();
    }
}
