using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Principal;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;
using SharpDX.XInput;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Dsp;

public class AppSettings
{
    public string AudioDeviceId { get; set; } = "default";
    public bool[] SelectedPads { get; set; } = new bool[4] { true, false, false, false };
    public float MinVibration { get; set; } = 0.30f;
    public float NoiseGate { get; set; } = 0.05f;
    public bool SmoothingEnabled { get; set; } = true;
    public bool BassModeOnly { get; set; } = false;

    // Direksiyon ve Pedal Eksen Eşlemeleri
    public int LxAxis { get; set; } = 0;
    public int LyAxis { get; set; } = 1;
    public int RxAxis { get; set; } = 2;
    public int RyAxis { get; set; } = 3;
    public int LtAxis { get; set; } = 4;
    public int RtAxis { get; set; } = 5;

    public bool LxInv { get; set; } = false;
    public bool LyInv { get; set; } = true;
    public bool RxInv { get; set; } = false;
    public bool RyInv { get; set; } = true;
    public bool LtInv { get; set; } = false;
    public bool RtInv { get; set; } = false;

    public float LsDz { get; set; } = 0.05f;
    public float RsDz { get; set; } = 0.05f;
    public float LtDz { get; set; } = 0.05f;
    public float RtDz { get; set; } = 0.05f;

    public float LsAdz { get; set; } = 0.0f;
    public float RsAdz { get; set; } = 0.0f;
    public float LtAdz { get; set; } = 0.0f;
    public float RtAdz { get; set; } = 0.0f;

    public string LtType { get; set; } = "full";
    public string RtType { get; set; } = "full";
}

class Program
{
    static string appVersion = "3.5.0-CMD-Native";
    static AppSettings settings = new AppSettings();
    static string settingsFile = "settings.json";

    // Haptic & Ses Durumları
    static float currentVibration = 0f;
    static float gameVibration = 0f;
    static float currentPeak = 0f;
    static float lastBassIntensity = 0f;
    static string currentAudioDeviceName = "Default";

    // Canlı Eksen ve Yüzde Değerleri (ASCII Arayüz İçin)
    static float liveLx = 0f, liveLy = 0f, liveRx = 0f, liveRy = 0f, liveLt = 0f, liveRt = 0f;
    static bool isRealPadConnected = false;

    static WasapiLoopbackCapture? capture;
    static MMDevice? currentAudioDevice;
    static BiQuadFilter? subBassFilter;
    static BiQuadFilter? midBassFilter;

    static ViGEmClient? vigemClient;
    static IXbox360Controller? virtualPad;
    static Controller[] realControllers = new Controller[4];

    static bool IsAdmin() => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    static void LoadSettings()
    {
        if (File.Exists(settingsFile)) {
            try {
                settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(settingsFile)) ?? new AppSettings();
            } catch { settings = new AppSettings(); }
        } else {
            SaveSettings();
        }
    }

    static void SaveSettings()
    {
        File.WriteAllText(settingsFile, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
    }

    static void Main(string[] args)
    {
        Console.Title = $"VIB-BOOST V{appVersion} - Zero Latency CMD Core";
        Console.CursorVisible = false;

        CheckDependencies();
        LoadSettings();

        // ViGEm Sanal Kol Bağlantısı
        vigemClient = new ViGEmClient();
        virtualPad = vigemClient.CreateXbox360Controller();
        virtualPad.FeedbackReceived += (s, e) => {
            gameVibration = Math.Max(e.LargeMotor, e.SmallMotor) / 255.0f;
        };
        virtualPad.Connect();

        for (int i = 0; i < 4; i++)
            realControllers[i] = new Controller((UserIndex)i);

        RestartAudioCapture();

        // 1. Sıfır Gecikmeli Ana Motor Döngüsü (Yüksek Öncelikli İş Parçacığı)
        Task.Run(() => CoreEngineLoop());

        // 2. Canlı CMD ASCII Görselleştirme Arayüzü
        Task.Run(() => ConsoleUILoop());

        // 3. Hafif Web Ayar Sunucusu (Sadece ayarlama ve kaydetme için)
        RunWebServer();
    }

    static void RestartAudioCapture()
    {
        if (capture != null) {
            capture.StopRecording();
            capture.Dispose();
            capture = null;
        }

        var enumerator = new MMDeviceEnumerator();
        if (settings.AudioDeviceId == "default" || string.IsNullOrEmpty(settings.AudioDeviceId)) {
            currentAudioDevice = enumerator.HasDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia) ? 
                                 enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia) : null;
        } else {
            currentAudioDevice = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
                                           .FirstOrDefault(d => d.ID == settings.AudioDeviceId);
        }

        currentAudioDeviceName = currentAudioDevice?.FriendlyName ?? "Default Audio Device";

        if (currentAudioDevice != null) {
            capture = new WasapiLoopbackCapture(currentAudioDevice);
            var format = capture.WaveFormat;
            
            subBassFilter = BiQuadFilter.LowPassFilter((float)format.SampleRate, 60f, 1f);
            midBassFilter = BiQuadFilter.BandPassFilterConstantPeakGain((float)format.SampleRate, 90f, 1f);

            capture.DataAvailable += (s, e) => {
                var buffer = new WaveBuffer(e.Buffer);
                int frameCount = e.BytesRecorded / 8;
                float subBassMax = 0f, midBassMax = 0f;

                for (int i = 0; i < frameCount; i++) {
                    float monoSample = (buffer.FloatBuffer[i * 2] + buffer.FloatBuffer[(i * 2) + 1]) / 2f;
                    if (settings.BassModeOnly && subBassFilter != null && midBassFilter != null) {
                        float subSample = subBassFilter.Transform(monoSample);
                        float midSample = midBassFilter.Transform(monoSample);
                        if (Math.Abs(subSample) > subBassMax) subBassMax = Math.Abs(subSample);
                        if (Math.Abs(midSample) > midBassMax) midBassMax = Math.Abs(midSample);
                    }
                }
                lastBassIntensity = Math.Min(1.0f, (subBassMax * 2.0f) + (midBassMax * 1.0f));
            };
            capture.StartRecording();
        }
    }

    static (float x, float y) ApplyRadialDeadzone(float x, float y, bool invX, bool invY, float dz, float adz)
    {
        float ax = invX ? -x : x;
        float ay = invY ? -y : y;
        float mag = (float)Math.Sqrt((ax * ax) + (ay * ay));
        if (mag < dz || mag == 0f) return (0f, 0f);
        if (mag > 1f) mag = 1f;
        float dirX = ax / mag;
        float dirY = ay / mag;
        float scaled = (mag - dz) / (1f - dz);
        scaled = adz + ((1f - adz) * scaled);
        return (dirX * Math.Clamp(scaled, 0f, 1f), dirY * Math.Clamp(scaled, 0f, 1f));
    }

    static float ApplyTriggerLogic(float val, bool inv, float dz, float adz, string type)
    {
        float v = val;
        if (type == "full") v = (val + 1f) / 2f;
        if (inv) v = 1f - v;
        if (v < dz) return 0f;
        float result = (v - dz) / (1f - dz);
        result = adz + ((1f - adz) * result);
        return Math.Clamp(result, 0f, 1f);
    }

    static void CoreEngineLoop()
    {
        float attackSpeed = 0.85f;
        float releaseSpeed = 0.06f;
        Thread.CurrentThread.Priority = ThreadPriority.Highest;

        while (true)
        {
            // Fiziksel Kontrolcü Okuma
            Controller? activeRealPad = null;
            for (int i = 0; i < 4; i++) {
                if (realControllers[i].IsConnected) {
                    activeRealPad = realControllers[i];
                    isRealPadConnected = true;
                    break;
                }
            }

            if (activeRealPad != null) {
                var state = activeRealPad.GetState().Gamepad;
                float rawLx = state.LeftThumbX / 32768f;
                float rawLy = state.LeftThumbY / 32768f;
                float rawRx = state.RightThumbX / 32768f;
                float rawRy = state.RightThumbY / 32768f;
                float rawLt = state.LeftTrigger / 255f;
                float rawRt = state.RightTrigger / 255f;

                var lStick = ApplyRadialDeadzone(rawLx, rawLy, settings.LxInv, settings.LyInv, settings.LsDz, settings.LsAdz);
                var rStick = ApplyRadialDeadzone(rawRx, rawRy, settings.RxInv, settings.RyInv, settings.RsDz, settings.RsAdz);
                
                liveLx = lStick.x;
                liveLy = lStick.y;
                liveRx = rStick.x;
                liveRy = rStick.y;
                liveLt = ApplyTriggerLogic(rawLt, settings.LtInv, settings.LtDz, settings.LtAdz, settings.LtType);
                liveRt = ApplyTriggerLogic(rawRt, settings.RtInv, settings.RtDz, settings.RtAdz, settings.RtType);

                if (virtualPad != null) {
                    virtualPad.SetAxisValue(Xbox360Axis.LeftThumbX, (short)(liveLx * 32767));
                    virtualPad.SetAxisValue(Xbox360Axis.LeftThumbY, (short)(liveLy * 32767));
                    virtualPad.SetAxisValue(Xbox360Axis.RightThumbX, (short)(liveRx * 32767));
                    virtualPad.SetAxisValue(Xbox360Axis.RightThumbY, (short)(liveRy * 32767));
                    virtualPad.SetSliderValue(Xbox360Slider.LeftTrigger, (byte)(liveLt * 255));
                    virtualPad.SetSliderValue(Xbox360Slider.RightTrigger, (byte)(liveRt * 255));

                    var b = state.Buttons;
                    virtualPad.SetButtonState(Xbox360Button.A, b.HasFlag(GamepadButtonFlags.A));
                    virtualPad.SetButtonState(Xbox360Button.B, b.HasFlag(GamepadButtonFlags.B));
                    virtualPad.SetButtonState(Xbox360Button.X, b.HasFlag(GamepadButtonFlags.X));
                    virtualPad.SetButtonState(Xbox360Button.Y, b.HasFlag(GamepadButtonFlags.Y));
                    virtualPad.SetButtonState(Xbox360Button.LeftShoulder, b.HasFlag(GamepadButtonFlags.LeftShoulder));
                    virtualPad.SetButtonState(Xbox360Button.RightShoulder, b.HasFlag(GamepadButtonFlags.RightShoulder));
                    virtualPad.SetButtonState(Xbox360Button.Back, b.HasFlag(GamepadButtonFlags.Back));
                    virtualPad.SetButtonState(Xbox360Button.Start, b.HasFlag(GamepadButtonFlags.Start));
                    virtualPad.SetButtonState(Xbox360Button.Up, b.HasFlag(GamepadButtonFlags.DPadUp));
                    virtualPad.SetButtonState(Xbox360Button.Down, b.HasFlag(GamepadButtonFlags.DPadDown));
                    virtualPad.SetButtonState(Xbox360Button.Left, b.HasFlag(GamepadButtonFlags.DPadLeft));
                    virtualPad.SetButtonState(Xbox360Button.Right, b.HasFlag(GamepadButtonFlags.DPadRight));
                    virtualPad.SetButtonState(Xbox360Button.LeftThumb, b.HasFlag(GamepadButtonFlags.LeftThumb));
                    virtualPad.SetButtonState(Xbox360Button.RightThumb, b.HasFlag(GamepadButtonFlags.RightThumb));
                }
            } else {
                isRealPadConnected = false;
            }

            // Ses Haptic Hesaplama
            float systemPeak = currentAudioDevice?.AudioMeterInformation.MasterPeakValue ?? 0f;
            currentPeak = settings.BassModeOnly ? lastBassIntensity : systemPeak;
            
            float audioTarget = 0f;
            if (currentPeak > settings.NoiseGate) {
                float normalized = (currentPeak - settings.NoiseGate) / (1f - settings.NoiseGate);
                audioTarget = settings.MinVibration + (normalized * normalized * (1f - settings.MinVibration));
            }
            
            float finalTarget = Math.Max(audioTarget, gameVibration);

            if (settings.SmoothingEnabled) {
                if (finalTarget > currentVibration) currentVibration += (finalTarget - currentVibration) * attackSpeed;
                else currentVibration -= (currentVibration - finalTarget) * releaseSpeed;
            } else {
                currentVibration = finalTarget;
            }

            currentVibration = Math.Clamp(currentVibration, 0f, 1f);
            ushort motorSpeed = (ushort)(currentVibration * 65535);

            // Seçili tüm fiziksel kollara aynı anda eşzamanlı titreşim gönder
            for (int i = 0; i < 4; i++) {
                if (settings.SelectedPads[i] && realControllers[i].IsConnected) {
                    realControllers[i].SetVibration(new Vibration { LeftMotorSpeed = motorSpeed, RightMotorSpeed = motorSpeed });
                }
            }

            Thread.Sleep(2); // Sıfıra yakın gecikme için 2ms döngü
        }
    }

    static string GetAsciiBar(float val, int width = 20)
    {
        int blocks = (int)(Math.Clamp((val + 1f) / 2f, 0f, 1f) * width);
        return "[" + new string('█', blocks) + new string('-', width - blocks) + "]";
    }

    static string GetPercentBar(float val, int width = 20)
    {
        int blocks = (int)(Math.Clamp(val, 0f, 1f) * width);
        return "[" + new string('█', blocks) + new string('-', width - blocks) + "]";
    }

    static void ConsoleUILoop()
    {
        while (true)
        {
            Console.SetCursorPosition(0, 0);
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("=================================================================");
            Console.WriteLine($"         VIB-BOOST V{appVersion} - CMD ZERO-LATENCY CORE         ");
            Console.WriteLine("=================================================================");

            Console.ResetColor();
            Console.Write("[ STATUS ] ");
            if (isRealPadConnected) { Console.ForegroundColor = ConsoleColor.Green; Console.WriteLine("DEVICE CONNECTED (Active)          "); }
            else { Console.ForegroundColor = ConsoleColor.Red; Console.WriteLine("SEARCHING WHEEL / GAMEPAD...     "); }
            Console.ResetColor();

            Console.WriteLine($"[ AUDIO  ] {currentAudioDeviceName.PadRight(49)}");
            Console.WriteLine("-----------------------------------------------------------------");
            Console.WriteLine(" LIVE AXES & PEDALS MONITORING (ASCII GUI):");
            
            // Direksiyon Ekseni (Sol X)
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write($" Steering (LX) : {liveLx:+0.00;-0.00; 0.00}  %{(int)(liveLx * 100),-4} ");
            Console.WriteLine(GetAsciiBar(liveLx, 18));

            // Gaz Ekseni (Sağ Tetik - RT)
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write($" Gas / RT      :             %{(int)(liveRt * 100),-4} ");
            Console.WriteLine(GetPercentBar(liveRt, 18));

            // Fren Ekseni (Sol Tetik - LT)
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Write($" Brake / LT    :             %{(int)(liveLt * 100),-4} ");
            Console.WriteLine(GetPercentBar(liveLt, 18));

            Console.ResetColor();
            Console.WriteLine("-----------------------------------------------------------------");
            Console.Write(" AUDIO IN      : ");
            Console.ForegroundColor = ConsoleColor.Blue;
            Console.Write($"%{(currentPeak * 100):00} ".PadLeft(5));
            Console.WriteLine(GetPercentBar(currentPeak, 28));

            Console.ForegroundColor = ConsoleColor.White;
            Console.Write(" VIB OUT       : ");
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write($"%{(currentVibration * 100):00} ".PadLeft(5));
            Console.WriteLine(GetPercentBar(currentVibration, 28));

            Console.ResetColor();
            Console.WriteLine("-----------------------------------------------------------------");
            Console.WriteLine(" Web Settings GUI: http://localhost:5000                         ");
            Console.WriteLine(" Save changes on Web GUI to update CMD execution instantly.      ");
            Console.WriteLine("=================================================================");

            Thread.Sleep(33);
        }
    }

    static void RunWebServer()
    {
        var builder = WebApplication.CreateBuilder(new string[0]);
        var app = builder.Build();

        app.UseStaticFiles();

        app.MapGet("/api/devices", () => {
            var enumerator = new MMDeviceEnumerator();
            var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            return devices.Select(d => new { id = d.ID, name = d.FriendlyName }).ToList();
        });

        app.MapGet("/api/settings", () => Results.Json(settings));

        app.MapPost("/api/settings", async (HttpContext context) => {
            var newSettings = await JsonSerializer.DeserializeAsync<AppSettings>(context.Request.Body);
            if (newSettings != null) {
                bool audioChanged = settings.AudioDeviceId != newSettings.AudioDeviceId;
                settings = newSettings;
                SaveSettings();
                if (audioChanged) RestartAudioCapture();
                return Results.Ok(new { success = true });
            }
            return Results.BadRequest();
        });

        app.Run("http://localhost:5000");
    }

    static void CheckDependencies()
    {
        try { using (var testClient = new ViGEmClient()) { } }
        catch (Exception) {
            if (!IsAdmin()) {
                Console.WriteLine("[ERROR] ViGEmBus Driver missing. Please run as Administrator!");
                Environment.Exit(0);
            }
            Process.Start(new ProcessStartInfo("powershell", "-Command \"Set-ExecutionPolicy Bypass -Scope Process -Force; iex ((New-Object System.Net.WebClient).DownloadString('https://community.chocolatey.org/install.ps1')); choco install vigembus -y\"") { Verb = "runas", UseShellExecute = true })?.WaitForExit();
            Console.WriteLine("Installation complete. Please restart your computer.");
            Environment.Exit(0);
        }
    }
}
