using System;
using System.Threading;
using System.Linq;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;
using SharpDX.XInput;

class Program
{
    static float multiplier = 3.0f; 

    static void UpdateDisplay(bool isConnected)
    {
        Console.Clear();
        Console.WriteLine("========================================");
        Console.WriteLine("    UNIVERSAL VIBRATION BOOST V1.5     ");
        Console.WriteLine("========================================");
        Console.WriteLine($"[ STATUS ] {(isConnected ? "CONNECTED (Active)" : "WAITING FOR CONTROLLER...")}");
        Console.WriteLine($"[ POWER  ] x{multiplier:F1}");
        Console.WriteLine("----------------------------------------");
        Console.WriteLine(" CONTROLS (Numpad):");
        Console.WriteLine(" [+] : Increase Power | [-] : Decrease Power");
        Console.WriteLine(" [0] : Kill Vibration | [Ctrl+C] : Exit");
        Console.WriteLine("========================================");
        Console.WriteLine(" TIP: Use 'HidHide' to hide your physical");
        Console.WriteLine(" controller from games for best results.");
    }

    static void Main(string[] args)
    {
        var client = new ViGEmClient();
        var virtualPad = client.CreateXbox360Controller();
        virtualPad.Connect();

        var controllers = new[] { new Controller(UserIndex.One), new Controller(UserIndex.Two), new Controller(UserIndex.Three) };
        Controller? realPad = controllers.FirstOrDefault(c => c.IsConnected);

        UpdateDisplay(realPad != null);

        // Vibration Feedback Loop
        virtualPad.FeedbackReceived += (s, e) => {
            if (realPad != null && realPad.IsConnected) {
                ushort left = (ushort)Math.Min(e.LargeMotor * 257 * multiplier, 65535);
                ushort right = (ushort)Math.Min(e.SmallMotor * 257 * multiplier, 65535);
                realPad.SetVibration(new Vibration { LeftMotorSpeed = left, RightMotorSpeed = right });
            }
        };

        while (true) {
            // Input Controls
            if (Console.KeyAvailable) {
                var key = Console.ReadKey(true).Key;
                if (key == ConsoleKey.Add || key == ConsoleKey.OemPlus) {
                    multiplier += 0.5f;
                    UpdateDisplay(realPad != null);
                }
                else if (key == ConsoleKey.Subtract || key == ConsoleKey.OemMinus) {
                    multiplier = Math.Max(0, multiplier - 0.5f);
                    UpdateDisplay(realPad != null);
                }
                else if (key == ConsoleKey.NumPad0 || key == ConsoleKey.D0) {
                    multiplier = 0;
                    UpdateDisplay(realPad != null);
                }
            }

            // Button Mapping
            if (realPad != null && realPad.IsConnected) {
                var state = realPad.GetState();
                var b = state.Gamepad.Buttons;

                virtualPad.SetButtonState(Xbox360Button.A, b.HasFlag(GamepadButtonFlags.A));
                virtualPad.SetButtonState(Xbox360Button.B, b.HasFlag(GamepadButtonFlags.B));
                virtualPad.SetButtonState(Xbox360Button.X, b.HasFlag(GamepadButtonFlags.X));
                virtualPad.SetButtonState(Xbox360Button.Y, b.HasFlag(GamepadButtonFlags.Y));
                virtualPad.SetButtonState(Xbox360Button.Start, b.HasFlag(GamepadButtonFlags.Start));
                virtualPad.SetButtonState(Xbox360Button.Back, b.HasFlag(GamepadButtonFlags.Back));
                virtualPad.SetButtonState(Xbox360Button.LeftShoulder, b.HasFlag(GamepadButtonFlags.LeftShoulder));
                virtualPad.SetButtonState(Xbox360Button.RightShoulder, b.HasFlag(GamepadButtonFlags.RightShoulder));
                virtualPad.SetButtonState(Xbox360Button.LeftThumb, b.HasFlag(GamepadButtonFlags.LeftThumb));
                virtualPad.SetButtonState(Xbox360Button.RightThumb, b.HasFlag(GamepadButtonFlags.RightThumb));
                virtualPad.SetButtonState(Xbox360Button.Up, b.HasFlag(GamepadButtonFlags.DPadUp));
                virtualPad.SetButtonState(Xbox360Button.Down, b.HasFlag(GamepadButtonFlags.DPadDown));
                virtualPad.SetButtonState(Xbox360Button.Left, b.HasFlag(GamepadButtonFlags.DPadLeft));
                virtualPad.SetButtonState(Xbox360Button.Right, b.HasFlag(GamepadButtonFlags.DPadRight));

                virtualPad.SetSliderValue(Xbox360Slider.LeftTrigger, state.Gamepad.LeftTrigger);
                virtualPad.SetSliderValue(Xbox360Slider.RightTrigger, state.Gamepad.RightTrigger);

                virtualPad.SetAxisValue(Xbox360Axis.LeftThumbX, state.Gamepad.LeftThumbX);
                virtualPad.SetAxisValue(Xbox360Axis.LeftThumbY, state.Gamepad.LeftThumbY);
                virtualPad.SetAxisValue(Xbox360Axis.RightThumbX, state.Gamepad.RightThumbX);
                virtualPad.SetAxisValue(Xbox360Axis.RightThumbY, state.Gamepad.RightThumbY);
            }
            
            Thread.Sleep(5);
        }
    }
}
