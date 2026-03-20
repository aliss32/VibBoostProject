using System;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Diagnostics;
using System.Security.Principal;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;
using SharpDX.XInput;
using NAudio.CoreAudioApi;

class Program
{
    static string appVersion = "1.0.1";
    static string owner = "alissgmr";
    
    static float minVibration = 0.30f; 
    static float noiseGate = 0.05f;    
    static float currentVibration = 0f;
    static float gameVibration = 0f;
    static float currentPeak = 0f;
    static bool smoothingEnabled = true;
    
    // Gecikme hissini azaltmak icin atak hizi artirildi (0.6 -> 0.85)
    static float attackSpeed = 0.85f;
    static float releaseSpeed = 0.06f;

    static bool isDeviceConnected = false;
    static string currentDeviceName = "Waiting...";

    static bool IsAdmin() => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    static void CheckDependencies()
    {
        try {
            using (var testClient = new ViGEmClient()) { }
        }
        catch (Exception)
        {
            Console.Clear();
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("!!! DEPENDENCY MISSING: ViGEmBus Driver not found !!!");
            
            if (!IsAdmin())
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("\n[ERROR] Administrative privileges required to install drivers.");
                Console.WriteLine("[FIX] Please Right-Click and 'Run as Administrator'.");
                Console.ResetColor();
                Console.WriteLine("\nPress any key to exit...");
                Console.ReadKey();
                Environment.Exit(0);
            }

            Console.WriteLine("\n[*] Attempting automatic installation via Chocolatey...");
            InstallViaChoco();
        }
    }

    static void InstallViaChoco()
    {
        string chocoInstall = "Set-ExecutionPolicy Bypass -Scope Process -Force; [System.Net.ServicePointManager]::SecurityProtocol = [System.Net.ServicePointManager]::SecurityProtocol -bor 3072; iex ((New-Object System.Net.WebClient).DownloadString('https://community.chocolatey.org/install.ps1'))";
        string installVigem = "choco install vigembus -y";

        Console.WriteLine("[1/2] Installing/Checking Chocolatey...");
        RunPowerShell(chocoInstall);
        
        Console.WriteLine("[2/2] Installing ViGEmBus Driver...");
        RunPowerShell(installVigem);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("\n[SUCCESS] Installation complete! A RESTART IS REQUIRED.");
        Console.ResetColor();
        Console.WriteLine("Please restart your computer and run the program again.");
        Console.WriteLine("\nPress any key to exit...");
        Console.ReadKey();
        Environment.Exit(0);
    }

    static void RunPowerShell(string command)
    {
        ProcessStartInfo psi = new ProcessStartInfo("powershell", $"-Command \"{command}\"")
        {
            Verb = "runas",
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Normal
        };
        Process.Start(psi)?.WaitForExit();
    }

    static string GetProgressBar(float percent, int width = 20)
    {
        int blocks = (int)(percent * width);
        return "[" + new string('█', blocks) + new string('-', width - blocks) + "]";
    }

    // Arayuz cizimi artik ana donguyu yavaslatmamasi icin ayri calisacak
    static void UILoop()
    {
        while (true)
        {
            Console.SetCursorPosition(0, 0);
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("============================================================");
            Console.WriteLine($"       VIB-BOOST V{appVersion} - HAPTIC LIVE DASHBOARD           ");
            Console.WriteLine($"       Developed by: {owner}                                ");
            Console.WriteLine("============================================================");
            
            Console.ResetColor();
            Console.Write("[ STATUS ] ");
            if (isDeviceConnected) { Console.ForegroundColor = ConsoleColor.Green; Console.WriteLine("CONNECTED (Active)     "); }
            else { Console.ForegroundColor = ConsoleColor.Red; Console.WriteLine("SEARCHING CONTROLLER..."); }
            Console.ResetColor();

            Console.WriteLine($"[ DEVICE ] {currentDeviceName.PadRight(40)}");
            Console.WriteLine("------------------------------------------------------------");

            // POWER yerine MIN VIB yazildi
            Console.WriteLine($"[ MIN VIB] %{(minVibration * 100):0}  {GetProgressBar(minVibration)} (Numpad +/-)");
            Console.WriteLine($"[ GATE   ] %{(noiseGate * 100):0}   {GetProgressBar(noiseGate)} (Numpad * / /)");
            Console.Write($"[ SMOOTH ] ");
            if (smoothingEnabled) { Console.ForegroundColor = ConsoleColor.Magenta; Console.Write("ON (Organic)   "); }
            else { Console.ForegroundColor = ConsoleColor.Yellow; Console.Write("OFF (Raw Data) "); }
            Console.ResetColor();
            Console.WriteLine(" (Numpad . / Del)");

            Console.WriteLine("------------------------------------------------------------");
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine(" LIVE MONITORING:");
            Console.Write(" AUDIO IN : ");
            Console.ForegroundColor = ConsoleColor.Blue;
            Console.Write($"%{(currentPeak * 100):00} ".PadLeft(5));
            Console.WriteLine(GetProgressBar(currentPeak, 30));
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write(" VIB OUT  : ");
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write($"%{(currentVibration * 100):00} ".PadLeft(5));
            Console.WriteLine(GetProgressBar(currentVibration, 30));

            Console.ResetColor();
            Console.WriteLine("------------------------------------------------------------");
            Console.WriteLine(" [Ctrl+C] to Exit | Low-Latency Haptic Engine               ");
            Console.WriteLine("============================================================");

            Thread.Sleep(30); // Ekrani saniyede ~33 kez guncelle (Titreşim döngüsünü etkilemez)
        }
    }

    static void Main(string[] args)
    {
        Console.Title = $"VIB-BOOST V{appVersion} - {owner}";
        Console.CursorVisible = false;

        CheckDependencies();

        ViGEmClient client = new ViGEmClient();
        var enumerator = new MMDeviceEnumerator();
        var audioDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        
        if (audioDevice != null) currentDeviceName = audioDevice.FriendlyName;

        var virtualPad = client.CreateXbox360Controller();
        virtualPad.FeedbackReceived += (s, e) => {
            gameVibration = Math.Max(e.LargeMotor, e.SmallMotor) / 255.0f;
        };

        virtualPad.Connect();
        var controllers = new[] { new Controller(UserIndex.One), new Controller(UserIndex.Two), new Controller(UserIndex.Three) };
        Controller realPad = controllers.FirstOrDefault(c => c.IsConnected);

        // UI'i tamamen farkli bir Thread'e atiyoruz (Gecikmeyi onlemek icin)
        Task.Run(() => UILoop());

        // Ana isi yapan Thread'in onceligini Windows'ta en uste aliyoruz
        Thread.CurrentThread.Priority = ThreadPriority.Highest;

        while (true) 
        {
            if (realPad == null || !realPad.IsConnected)
                realPad = controllers.FirstOrDefault(c => c.IsConnected);

            isDeviceConnected = realPad != null && realPad.IsConnected;

            if (Console.KeyAvailable) {
                var key = Console.ReadKey(true).Key;
                if (key == ConsoleKey.Add || key == ConsoleKey.OemPlus) minVibration = Math.Min(1.0f, minVibration + 0.05f);
                else if (key == ConsoleKey.Subtract || key == ConsoleKey.OemMinus) minVibration = Math.Max(0.0f, minVibration - 0.05f);
                else if (key == ConsoleKey.Multiply) noiseGate = Math.Min(0.5f, noiseGate + 0.01f);
                else if (key == ConsoleKey.Divide) noiseGate = Math.Max(0.01f, noiseGate - 0.01f);
                else if (key == ConsoleKey.Decimal || key == ConsoleKey.Separator || key == ConsoleKey.Delete) smoothingEnabled = !smoothingEnabled;
            }

            if (isDeviceConnected) 
            {
                currentPeak = audioDevice?.AudioMeterInformation.MasterPeakValue ?? 0f;
                float audioTarget = 0f;
                
                if (currentPeak > noiseGate) {
                    float normalized = (currentPeak - noiseGate) / (1f - noiseGate);
                    audioTarget = minVibration + (normalized * normalized * (1f - minVibration));
                }
                
                float finalTarget = Math.Max(audioTarget, gameVibration);

                if (smoothingEnabled) {
                    if (finalTarget > currentVibration) currentVibration += (finalTarget - currentVibration) * attackSpeed;
                    else currentVibration -= (currentVibration - finalTarget) * releaseSpeed;
                } else {
                    currentVibration = finalTarget;
                }

                currentVibration = Math.Clamp(currentVibration, 0f, 1f);
                ushort motorSpeed = (ushort)(currentVibration * 65535);
                realPad.SetVibration(new Vibration { LeftMotorSpeed = motorSpeed, RightMotorSpeed = motorSpeed });

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
            
            // UI artik baska thread'de oldugu icin burada counter vs. yok
            Thread.Sleep(5); // Sadece saf titresim islemi icin gereken minimal bekleme
        }
    }
}
