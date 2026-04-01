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
using NAudio.Wave;
using NAudio.Dsp; // GERCEK BASS FILTRELERI ICIN EKLENDI

class Program
{
    static string appVersion = "1.0.4";
    static string owner = "alissgmr";
    
    static float minVibration = 0.30f; 
    static float noiseGate = 0.05f;    
    static float currentVibration = 0f;
    static float gameVibration = 0f;
    static float currentPeak = 0f;
    static bool smoothingEnabled = true;
    static bool bassModeOnly = false; // NumPad 0 ile kontrol edilecek
    
    static float attackSpeed = 0.85f;
    static float releaseSpeed = 0.06f;

    static bool isDeviceConnected = false;
    static string currentDeviceName = "Waiting...";
    
    static WasapiLoopbackCapture? capture;
    static float lastBassIntensity = 0f;

    // Gercek Ekolayzir Filtreleri
    static BiQuadFilter? subBassFilter;
    static BiQuadFilter? midBassFilter;

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
        int blocks = (int)(Math.Clamp(percent, 0f, 1f) * width);
        return "[" + new string('█', blocks) + new string('-', width - blocks) + "]";
    }

    static void Main(string[] args)
    {
        Console.Title = $"VIB-BOOST V{appVersion} - DSP Haptic Engine";
        Console.CursorVisible = false;

        CheckDependencies();

        ViGEmClient client = new ViGEmClient();
        var enumerator = new MMDeviceEnumerator();
        
        // NULL KONTROLLERI (CS8600 / CS8602 Uyarilarini Cözer)
        MMDevice? audioDevice = enumerator.HasDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia) 
            ? enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia) 
            : null;

        currentDeviceName = audioDevice?.FriendlyName ?? "No Audio Device Found";

        capture = new WasapiLoopbackCapture();
        var format = capture.WaveFormat;

        // DSP FILTRELERINI OLUSTUR (Ornekleme hizina gore)
        // Sub-Bass: 60Hz altindaki her seyi gecirir
        subBassFilter = BiQuadFilter.LowPassFilter((float)format.SampleRate, 60f, 1f);
        
        // Mid-Bass: Sadece 60Hz ile 120Hz arasini gucendirir (BandPass)
        midBassFilter = BiQuadFilter.BandPassFilterConstantPeakGain((float)format.SampleRate, 90f, 1f);

        capture.DataAvailable += (s, e) => {
            if (!bassModeOnly || subBassFilter == null || midBassFilter == null) return;
            
            var buffer = new WaveBuffer(e.Buffer);
            // Cift kanal (Stereo) float verisini frame frame islemek icin / 8 (4 byte * 2 kanal)
            int frameCount = e.BytesRecorded / 8; 
            
            float subBassMax = 0f;
            float midBassMax = 0f;

            for (int i = 0; i < frameCount; i++)
            {
                // Sol ve sag kanali alip Mono'ya cevir
                float sampleL = buffer.FloatBuffer[i * 2];
                float sampleR = buffer.FloatBuffer[(i * 2) + 1];
                float monoSample = (sampleL + sampleR) / 2f;

                // Gercek frekans filtrelerinden gecir
                float subSample = subBassFilter.Transform(monoSample);
                float midSample = midBassFilter.Transform(monoSample);

                if (Math.Abs(subSample) > subBassMax) subBassMax = Math.Abs(subSample);
                if (Math.Abs(midSample) > midBassMax) midBassMax = Math.Abs(midSample);
            }

            // GUC DAGILIMI: Sub-Bass cok guclu titretir, Mid-Bass orta titretir
            float weightedIntensity = (subBassMax * 2.0f) + (midBassMax * 1.0f);
            
            lastBassIntensity = Math.Min(1.0f, weightedIntensity); 
        };
        capture.StartRecording();

        var virtualPad = client.CreateXbox360Controller();
        virtualPad.FeedbackReceived += (s, e) => {
            gameVibration = Math.Max(e.LargeMotor, e.SmallMotor) / 255.0f;
        };
        virtualPad.Connect();

        var controllers = new[] { new Controller(UserIndex.One), new Controller(UserIndex.Two), new Controller(UserIndex.Three) };
        Controller? realPad = controllers.FirstOrDefault(c => c.IsConnected);

        Task.Run(() => UILoop());
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
                else if (key == ConsoleKey.Decimal || key == ConsoleKey.Delete) smoothingEnabled = !smoothingEnabled;
                else if (key == ConsoleKey.NumPad0 || key == ConsoleKey.D0) bassModeOnly = !bassModeOnly; // NUMPAD 0 ILE BASS MODU AC/KAPA
            }

            if (isDeviceConnected && realPad != null) 
            {
                float systemPeak = audioDevice?.AudioMeterInformation.MasterPeakValue ?? 0f;
                currentPeak = bassModeOnly ? lastBassIntensity : systemPeak;
                
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

                ForwardInputs(realPad, virtualPad);
            }
            Thread.Sleep(5); 
        }
    }

    static void UILoop()
    {
        while (true)
        {
            Console.SetCursorPosition(0, 0);
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("============================================================");
            Console.WriteLine($"       VIB-BOOST V{appVersion} - HAPTIC LIVE DASHBOARD           ");
            Console.WriteLine("============================================================");
            
            Console.ResetColor();
            Console.Write("[ STATUS ] ");
            if (isDeviceConnected) { Console.ForegroundColor = ConsoleColor.Green; Console.WriteLine("CONNECTED (Active)     "); }
            else { Console.ForegroundColor = ConsoleColor.Red; Console.WriteLine("SEARCHING CONTROLLER..."); }
            Console.ResetColor();

            Console.WriteLine($"[ DEVICE ] {currentDeviceName.PadRight(40)}");
            Console.WriteLine("------------------------------------------------------------");

            Console.WriteLine($"[ MIN VIB] %{(minVibration * 100):0}  {GetProgressBar(minVibration)} (Numpad +/-)");
            Console.WriteLine($"[ GATE   ] %{(noiseGate * 100):0}   {GetProgressBar(noiseGate)} (Numpad * / /)");
            
            Console.Write($"[ MODE   ] ");
            if (bassModeOnly) { Console.ForegroundColor = ConsoleColor.Red; Console.Write("REAL DSP BASS ONLY "); }
            else { Console.ForegroundColor = ConsoleColor.Blue; Console.Write("FULL SPECTRUM      "); }
            Console.ResetColor();
            Console.WriteLine(" (Num 0)");

            Console.Write($"[ SMOOTH ] ");
            if (smoothingEnabled) { Console.ForegroundColor = ConsoleColor.Magenta; Console.Write("ON (Organic)   "); }
            else { Console.ForegroundColor = ConsoleColor.Yellow; Console.Write("OFF (Raw Data) "); }
            Console.ResetColor();
            Console.WriteLine(" (Del)");

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
            Console.WriteLine(" [Ctrl+C] to Exit | DSP Engineering by alissgmr             ");
            Console.WriteLine("============================================================");

            Thread.Sleep(33);
        }
    }

    static void ForwardInputs(Controller real, IXbox360Controller virtualP) {
        var s = real.GetState().Gamepad;
        var b = s.Buttons;
        virtualP.SetButtonState(Xbox360Button.A, b.HasFlag(GamepadButtonFlags.A));
        virtualP.SetButtonState(Xbox360Button.B, b.HasFlag(GamepadButtonFlags.B));
        virtualP.SetButtonState(Xbox360Button.X, b.HasFlag(GamepadButtonFlags.X));
        virtualP.SetButtonState(Xbox360Button.Y, b.HasFlag(GamepadButtonFlags.Y));
        virtualP.SetButtonState(Xbox360Button.Start, b.HasFlag(GamepadButtonFlags.Start));
        virtualP.SetButtonState(Xbox360Button.Back, b.HasFlag(GamepadButtonFlags.Back));
        virtualP.SetButtonState(Xbox360Button.LeftShoulder, b.HasFlag(GamepadButtonFlags.LeftShoulder));
        virtualP.SetButtonState(Xbox360Button.RightShoulder, b.HasFlag(GamepadButtonFlags.RightShoulder));
        virtualP.SetButtonState(Xbox360Button.LeftThumb, b.HasFlag(GamepadButtonFlags.LeftThumb));
        virtualP.SetButtonState(Xbox360Button.RightThumb, b.HasFlag(GamepadButtonFlags.RightThumb));
        virtualP.SetButtonState(Xbox360Button.Up, b.HasFlag(GamepadButtonFlags.DPadUp));
        virtualP.SetButtonState(Xbox360Button.Down, b.HasFlag(GamepadButtonFlags.DPadDown));
        virtualP.SetButtonState(Xbox360Button.Left, b.HasFlag(GamepadButtonFlags.DPadLeft));
        virtualP.SetButtonState(Xbox360Button.Right, b.HasFlag(GamepadButtonFlags.DPadRight));
        virtualP.SetSliderValue(Xbox360Slider.LeftTrigger, s.LeftTrigger);
        virtualP.SetSliderValue(Xbox360Slider.RightTrigger, s.RightTrigger);
        virtualP.SetAxisValue(Xbox360Axis.LeftThumbX, s.LeftThumbX);
        virtualP.SetAxisValue(Xbox360Axis.LeftThumbY, s.LeftThumbY);
        virtualP.SetAxisValue(Xbox360Axis.RightThumbX, s.RightThumbX);
        virtualP.SetAxisValue(Xbox360Axis.RightThumbY, s.RightThumbY);
    }
}
