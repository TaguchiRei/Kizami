namespace UsefulTools.Utility.Runtime.Utility
{
    public interface IRecyclable
    {
        int RecycleId { get; set; }
        void OnRecycle();
    }
}