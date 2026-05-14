using UnityEngine.InputSystem;

public readonly struct InputContext<T> where T : unmanaged
{
    public readonly InputActionPhase Phase;
    public readonly T Value;

    public InputContext(InputActionPhase phase, T value)
    {
        Phase = phase;
        Value = value;
    }

    public bool IsActive    => Phase != InputActionPhase.Disabled
                               && Phase != InputActionPhase.Waiting;
    public bool IsPerformed => Phase == InputActionPhase.Performed;
    public bool IsCanceled  => Phase == InputActionPhase.Canceled;
    public bool IsStarted   => Phase == InputActionPhase.Started;
}