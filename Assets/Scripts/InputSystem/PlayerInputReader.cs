using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>Snapshot of the player's raw input for one frame.</summary>
public struct PlayerInputState
{
    /// <summary>WASD as a vector. Each axis is -1, 0 or +1.</summary>
    public Vector2 Move;
    public bool JumpPressed;   // Space, this frame
    public bool JumpHeld;      // Space, held
    public bool DashPressed;   // Shift, this frame
    public bool GripHeld;      // Ctrl, held
}

/// <summary>
/// Single place that reads the keyboard. Works with both the new Input System and the legacy Input Manager,
/// so the controller itself never touches an input API.
/// Bindings: WASD = move / aim dash / climb, Space = jump, Shift = dash, Ctrl = wall grip.
/// </summary>
public static class PlayerInputReader
{
    public static PlayerInputState Read()
    {
        var s = new PlayerInputState();
#if ENABLE_INPUT_SYSTEM
        var k = Keyboard.current;
        if (k == null) return s;

        s.Move = new Vector2(
            (k.dKey.isPressed ? 1f : 0f) - (k.aKey.isPressed ? 1f : 0f),
            (k.wKey.isPressed ? 1f : 0f) - (k.sKey.isPressed ? 1f : 0f));
        s.JumpPressed = k.spaceKey.wasPressedThisFrame;
        s.JumpHeld = k.spaceKey.isPressed;
        s.DashPressed = k.leftShiftKey.wasPressedThisFrame || k.rightShiftKey.wasPressedThisFrame;
        s.GripHeld = k.leftCtrlKey.isPressed || k.rightCtrlKey.isPressed;
#else
        s.Move = new Vector2(
            (Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f),
            (Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f));
        s.JumpPressed = Input.GetKeyDown(KeyCode.Space);
        s.JumpHeld = Input.GetKey(KeyCode.Space);
        s.DashPressed = Input.GetKeyDown(KeyCode.LeftShift) || Input.GetKeyDown(KeyCode.RightShift);
        s.GripHeld = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
#endif
        return s;
    }
}
