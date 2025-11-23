using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;

public class UnityInputProvider : MonoBehaviour, IInputProvider
{
    public event Action<Vector2, int> Begin;
    public event Action<Vector2, int> Move;
    public event Action<Vector2, int> End;

    private bool enabledInternal = false;

    private void OnEnable()
    {
        Enable();
    }

    private void OnDisable()
    {
        Disable();
    }

    public void Enable()
    {
        if (enabledInternal) return;
        enabledInternal = true;
        EnhancedTouchSupport.Enable();
    }

    public void Disable()
    {
        if (!enabledInternal) return;
        enabledInternal = false;
        EnhancedTouchSupport.Disable();
    }

    private void Update()
    {
        if (!enabledInternal) return;

        // Touch (EnhancedTouch)
        if (UnityEngine.InputSystem.EnhancedTouch.Touch.activeTouches.Count > 0)
        {
            foreach (var t in UnityEngine.InputSystem.EnhancedTouch.Touch.activeTouches)
            {
                var id = (int)t.touchId;
                if (t.phase == UnityEngine.InputSystem.TouchPhase.Began)
                    Begin?.Invoke(t.screenPosition, id);
                else if (t.phase == UnityEngine.InputSystem.TouchPhase.Moved || t.phase == UnityEngine.InputSystem.TouchPhase.Stationary)
                    Move?.Invoke(t.screenPosition, id);
                else if (t.phase == UnityEngine.InputSystem.TouchPhase.Ended || t.phase == UnityEngine.InputSystem.TouchPhase.Canceled)
                    End?.Invoke(t.screenPosition, id);
            }
            return;
        }

        // Mouse fallback
        var mouse = Mouse.current;
        if (mouse != null)
        {
            if (mouse.leftButton.wasPressedThisFrame)
                Begin?.Invoke(mouse.position.ReadValue(), -1);
            else if (mouse.leftButton.isPressed)
                Move?.Invoke(mouse.position.ReadValue(), -1);
            else if (mouse.leftButton.wasReleasedThisFrame)
                End?.Invoke(mouse.position.ReadValue(), -1);
        }
    }
}
