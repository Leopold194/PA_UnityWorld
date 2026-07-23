using UnityEngine;
using UnityEngine.InputSystem;

public class GamepadInputSender : MonoBehaviour
{
    void Update()
    {
        var pad = Gamepad.current;
        if (pad == null) return;

        Vector2 move = pad.leftStick.ReadValue();
        Vector2 look = pad.rightStick.ReadValue();

        var msg = new PlayerInputMsg
        {
            vx = move.x,
            vy = move.y,
            look_x = look.x,
            look_y = look.y,
            kick = pad.buttonSouth.isPressed,
            dash = pad.buttonWest.isPressed
        };

        GameNetworkClient.Instance.SendInput(msg);
    }
}