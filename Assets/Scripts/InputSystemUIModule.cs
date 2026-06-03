using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// Custom BaseInput implementation that uses the new Input System package
/// instead of the legacy UnityEngine.Input class.
/// This resolves the InvalidOperationException when Input System is enabled in Player Settings.
/// </summary>
public class InputSystemUIModule : BaseInput
{
    public override bool GetMouseButtonDown(int button)
    {
        return Mouse.current != null && 
               (button == 0 ? Mouse.current.leftButton.wasPressedThisFrame : 
                button == 1 ? Mouse.current.rightButton.wasPressedThisFrame :
                button == 2 ? Mouse.current.middleButton.wasPressedThisFrame : false);
    }

    public override bool GetMouseButtonUp(int button)
    {
        return Mouse.current != null && 
               (button == 0 ? Mouse.current.leftButton.wasReleasedThisFrame : 
                button == 1 ? Mouse.current.rightButton.wasReleasedThisFrame :
                button == 2 ? Mouse.current.middleButton.wasReleasedThisFrame : false);
    }

    public override bool GetMouseButton(int button)
    {
        return Mouse.current != null && 
               (button == 0 ? Mouse.current.leftButton.isPressed : 
                button == 1 ? Mouse.current.rightButton.isPressed :
                button == 2 ? Mouse.current.middleButton.isPressed : false);
    }

    public override Vector2 mousePosition
    {
        get { return Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero; }
    }

    public override Vector2 mouseScrollDelta
    {
        get { return Mouse.current != null ? Mouse.current.scroll.ReadValue() : Vector2.zero; }
    }

    public override bool mousePresent
    {
        get { return Mouse.current != null; }
    }

    public override bool touchSupported
    {
        get { return Touchscreen.current != null; }
    }

    public override int touchCount
    {
        get { return Touchscreen.current != null ? Touchscreen.current.touches.Count : 0; }
    }

    public override Touch GetTouch(int index)
    {
        if (Touchscreen.current != null && index >= 0 && index < Touchscreen.current.touches.Count)
        {
            var touchControl = Touchscreen.current.touches[index];
            return new Touch
            {
                fingerId = index,
                position = touchControl.position.ReadValue(),
                phase = (UnityEngine.TouchPhase)UnityEngine.InputSystem.TouchPhase.Moved,
                pressure = touchControl.pressure.ReadValue(),
                radius = 0,
                radiusVariance = 0,
                deltaPosition = touchControl.delta.ReadValue(),
                deltaTime = Time.deltaTime,
                tapCount = 1
            };
        }
        return new Touch();
    }

    public override float GetAxisRaw(string axisName)
    {
        // Maps Input Manager axes to Input System
        return axisName switch
        {
            "Horizontal" => Keyboard.current != null ? 
                (Keyboard.current.dKey.isPressed ? 1 : 0) + (Keyboard.current.aKey.isPressed ? -1 : 0) : 0,
            "Vertical" => Keyboard.current != null ? 
                (Keyboard.current.wKey.isPressed ? 1 : 0) + (Keyboard.current.sKey.isPressed ? -1 : 0) : 0,
            _ => 0
        };
    }

    public override bool GetButtonDown(string buttonName)
    {
        // Maps common button names to keyboard inputs
        return buttonName switch
        {
            "Submit" => Keyboard.current != null && Keyboard.current.enterKey.wasPressedThisFrame,
            "Cancel" => Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame,
            "Fire1" => GetMouseButtonDown(0),
            "Fire2" => GetMouseButtonDown(1),
            "Fire3" => GetMouseButtonDown(2),
            _ => false
        };
    }

    public override string compositionString
    {
        get { return ""; }
    }

    public override IMECompositionMode imeCompositionMode
    {
        get { return IMECompositionMode.Auto; }
        set { }
    }

    public override Vector2 compositionCursorPos
    {
        get { return Vector2.zero; }
        set { }
    }
}
