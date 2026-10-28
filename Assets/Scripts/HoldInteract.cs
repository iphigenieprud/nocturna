using UnityEngine;

// Shared "charge with E" interaction for candles and memories:
// shows a gentle prompt with an empty circle; keeping E down fills the circle.
// If the gun is out, it's lowered automatically while you charge, and comes back after.
// (Not a component: Candle and Memento each use one.)
public class HoldInteract
{
    public const float HoldTime = 1.5f;   // seconds to fill the circle

    float progress;
    bool showing;
    int putAwayFrom = -1;                 // what you were holding before we lowered the gun

    // Call every frame. Returns true on the frame the circle fills.
    public bool Tick(Object owner, bool close, string prompt, KeyCode key) { return Tick(owner, close, null, prompt, key); }

    // title = what it is (shown on top), prompt = what to do (shown under it)
    public bool Tick(Object owner, bool close, string title, string prompt, KeyCode key)
    {
        UIManager ui = UIManager.Instance;
        if (!close)
        {
            if (showing) Clear(owner);
            return false;
        }
        showing = true;
        if (ui != null) ui.ShowPrompt(owner, prompt, title);

        if (Input.GetKey(key))
        {
            // Starting to charge: lower the gun for you
            WeaponSwitcher ws = WeaponSwitcher.Instance;
            if (putAwayFrom < 0 && ws != null && ws.Current != 0)
            {
                putAwayFrom = ws.Current;
                ws.Select(0, false);
            }
            progress += Time.deltaTime / HoldTime;
        }
        else
        {
            // Let go: the circle drains back, and the gun returns once it's empty
            progress = Mathf.Max(0f, progress - Time.deltaTime * 3f / HoldTime);
            if (progress <= 0f) GiveGunBack();
        }

        if (ui != null) ui.SetHold(owner, progress);

        if (progress >= 1f)
        {
            Clear(owner);
            return true;
        }
        return false;
    }

    public void Clear(Object owner)
    {
        showing = false;
        progress = 0f;
        GiveGunBack();
        UIManager ui = UIManager.Instance;
        if (ui != null)
        {
            ui.HidePrompt(owner);
            ui.SetHold(owner, 0f);
        }
    }

    void GiveGunBack()
    {
        if (putAwayFrom < 0) return;
        if (WeaponSwitcher.Instance != null && WeaponSwitcher.Instance.Current == 0)
            WeaponSwitcher.Instance.Select(putAwayFrom, false);
        putAwayFrom = -1;
    }
}