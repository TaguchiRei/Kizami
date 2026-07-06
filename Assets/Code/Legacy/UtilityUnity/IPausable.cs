namespace UsefulTools.UtilityUnity.Runtime.Pause
{
    public interface IPausable
    {
        bool IsPaused { get; }
        void Pause();
        void Resume();
    }
}