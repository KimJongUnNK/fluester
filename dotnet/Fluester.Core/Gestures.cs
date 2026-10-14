namespace Fluester.Core;

/// <summary>Erkennt aus Drücken/Loslassen der Diktat-Taste: halten (Push-to-talk), Doppeltipp (freihändig), Abbruch.
/// Reine Zustandsmaschine ohne Systemzugriff, damit sie testbar ist.</summary>
public sealed class HotkeyGestures
{
    public enum Kind { None, StartHold, StopAndProcess, Cancel, SwitchToHandsFree }

    public readonly record struct Action(Kind Kind, bool Command = false)
    {
        public static readonly Action None = new(Kind.None);
        public static Action Start(bool command) => new(Kind.StartHold, command);
        public static readonly Action Stop = new(Kind.StopAndProcess);
        public static readonly Action CancelIt = new(Kind.Cancel);
        public static readonly Action HandsFree = new(Kind.SwitchToHandsFree);
    }

    public enum GestureState { Idle, Holding, WaitingSecondTap, HandsFree }

    public GestureState State { get; private set; } = GestureState.Idle;
    public bool DoubleTapEnabled { get; set; } = true;
    /// <summary>Kürzer gehalten = Tipp.</summary>
    public double TapMaxDuration { get; set; } = 0.28;
    /// <summary>Abstand zwischen zwei Tipps.</summary>
    public double DoubleTapWindow { get; set; } = 0.38;

    double pressTime;
    double lastTapTime = -10;
    bool otherKeyWhileHeld;
    /// <summary>Das Loslassen des zweiten Tipps (Doppeltipp) beendet nicht gleich wieder.</summary>
    bool ignoreNextRelease;

    public Action Press(double t, bool command)
    {
        switch (State)
        {
            case GestureState.Idle:
                State = GestureState.Holding;
                pressTime = t;
                otherKeyWhileHeld = false;
                return Action.Start(command);
            case GestureState.WaitingSecondTap:
                if (t - lastTapTime <= DoubleTapWindow && DoubleTapEnabled)
                {
                    State = GestureState.HandsFree;
                    ignoreNextRelease = true;
                    return Action.HandsFree;
                }
                State = GestureState.Holding;
                pressTime = t;
                otherKeyWhileHeld = false;
                return Action.Start(command);
            case GestureState.HandsFree:
                pressTime = t;
                return Action.None;
            default:
                return Action.None;
        }
    }

    public Action Release(double t)
    {
        switch (State)
        {
            case GestureState.Holding:
                double held = t - pressTime;
                if (otherKeyWhileHeld && held < 1.0)
                {
                    State = GestureState.Idle;
                    return Action.CancelIt;
                }
                if (held < TapMaxDuration)
                {
                    if (DoubleTapEnabled)
                    {
                        State = GestureState.WaitingSecondTap;
                        lastTapTime = t;
                        return Action.None;
                    }
                    State = GestureState.Idle;
                    return Action.CancelIt;
                }
                State = GestureState.Idle;
                return Action.Stop;
            case GestureState.HandsFree:
                if (ignoreNextRelease)
                {
                    ignoreNextRelease = false;
                    return Action.None;
                }
                State = GestureState.Idle;
                return Action.Stop;
            default:
                return Action.None;
        }
    }

    /// <summary>Kein zweiter Tipp innerhalb des Fensters → Aufnahme verwerfen.</summary>
    public Action Tick(double t)
    {
        if (State == GestureState.WaitingSecondTap && t - lastTapTime > DoubleTapWindow)
        {
            State = GestureState.Idle;
            return Action.CancelIt;
        }
        return Action.None;
    }

    /// <summary>Andere Taste während gehaltener Diktat-Taste (z. B. Strg+C) → kein Diktat.</summary>
    public void OtherKeyPressed()
    {
        if (State == GestureState.Holding) otherKeyWhileHeld = true;
    }

    public void Reset()
    {
        State = GestureState.Idle;
        otherKeyWhileHeld = false;
        ignoreNextRelease = false;
    }
}
