namespace UsefulTools.UtilityUnity.Runtime.Initialize
{
    public interface IInjectable<in T>
    {
        void Inject(T obj);
    }

    public interface IInjectable<in T1, in T2>
    {
        void Inject(T1 obj1, T2 obj2);
    }

    public interface IInjectable<in T1, in T2, in T3>
    {
        void Inject(T1 obj1, T2 obj2, T3 obj3);
    }

    public interface IInjectable<in T1, in T2, in T3, in T4>
    {
        void Inject(T1 obj1, T2 obj2, T3 obj3, T4 obj4);
    }
}