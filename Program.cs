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
using System.Net.WebSockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;
using SharpDX.XInput;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Dsp;

// --- AYARLAR MODELİ ---
public class AppSettings
{
    public string AudioDeviceId { get; set; } = "default";
    public bool[] SelectedPads { get; set; } = new bool[4] { true, false, false, false };
    public float MinVibration { get; set; } = 0.30f;
    public float NoiseGate { get; set; } = 0.05f;
    public bool SmoothingEnabled { get; set; } = true;
    public bool BassModeOnly { get; set; } = false;
}

// --- WEB GAMEPAD PAYLOAD MODELİ ---
public class WebGamepadPayload
{
    public Dictionary<string, float> axes { get; set; } = new();
    public Dictionary<string, float> triggers { get; set; } = new();
    public Dictionary<string, int> buttons { get; set; } = new();
}

class Program
{
    static string appVersion = "2.0.0-Web";
    static AppSettings settings = new AppSettings();
    static string settingsFile = "settings.json";

    // Haptic ve DSP Durumları
    static float currentVibration = 0f;
    static float gameVibration = 0f;
    static float currentPeak = 0f;
    static float lastBassIntensity = 0f;
    
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
        if (File.Exists(settingsFile))
            settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(settingsFile)) ?? new AppSettings();
    }

    static void SaveSettings()
    {
        File.WriteAllText(settingsFile, JsonSerializer.Serialize(settings));
    }

    static void Main(string[] args)
    {
        CheckDependencies();
        LoadSettings();

        // ViGEm Sanal Kol Kurulumu
        vigemClient = new ViGEmClient();
        virtualPad = vigemClient.CreateXbox360Controller();
        virtualPad.FeedbackReceived += (s, e) => {
            gameVibration = Math.Max(e.LargeMotor, e.SmallMotor) / 255.0f;
        };
        virtualPad.Connect();

        // Fiziksel XInput Kolları
        for (int i = 0; i < 4; i++)
            realControllers[i] = new Controller((UserIndex)i);

        // Sesi Başlat
        RestartAudioCapture();

        // Haptic Motor Döngüsünü Başlat
        Task.Run(() => HapticLoop());

        // Kestrel Web Sunucusunu Başlat
        var builder = WebApplication.CreateBuilder(args);
        var app = builder.Build();

        app.UseWebSockets();
        app.UseDefaultFiles(); 
        app.UseStaticFiles(); // wwwroot klasöründeki HTML'i sunmak için

        // API: Cihaz Listesi (Ses)
        app.MapGet("/api/devices", () => {
            var enumerator = new MMDeviceEnumerator();
            var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            return devices.Select(d => new { id = d.ID, name = d.FriendlyName }).ToList();
        });

        // API: Mevcut Ayarları Al
        app.MapGet("/api/settings", () => Results.Json(settings));

        // API: Ayarları Kaydet
        app.MapPost("/api/settings", async (HttpContext context) => {
            var newSettings = await JsonSerializer.DeserializeAsync<AppSettings>(context.Request.Body);
            if (newSettings != null)
            {
                bool audioChanged = settings.AudioDeviceId != newSettings.AudioDeviceId;
                settings = newSettings;
                SaveSettings();
                
                if (audioChanged) RestartAudioCapture();
                return Results.Ok(new { success = true });
            }
            return Results.BadRequest();
        });

        // API: Canlı İstatistikler (Dashboard için)
        app.MapGet("/api/status", () => Results.Json(new {
            peak = currentPeak,
            vib = currentVibration,
            gameVib = gameVibration,
            connectedPads = realControllers.Select(c => c.IsConnected).ToArray()
        }));

        // WEBSOCKET: Web Gamepad'den gelen anlık kontrolleri Sanal Kola aktar
        app.Map("/ws", async context => {
            if (context.WebSockets.IsWebSocketRequest)
            {
                using var ws = await context.WebSockets.AcceptWebSocketAsync();
                var buffer = new byte[1024 * 4];
                while (ws.State == WebSocketState.Open)
                {
                    var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        try {
                            var payload = JsonSerializer.Deserialize<WebGamepadPayload>(json);
                            if (payload != null) ApplyWebGamepadToVirtualPad(payload);
                        } catch { }
                    }
                }
            }
            else { context.Response.StatusCode = 400; }
        });

        Console.WriteLine($"VibBoost & Web Gamepad V{appVersion} Started!");
        Console.WriteLine("Arayüz İçin Tarayıcıda Açın: http://localhost:5000");
        app.Run("http://localhost:5000");
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
                    if (settings.BassModeOnly) {
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

    static void HapticLoop()
    {
        float attackSpeed = 0.85f;
        float releaseSpeed = 0.06f;
        Thread.CurrentThread.Priority = ThreadPriority.Highest;

        while (true)
        {
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

            // SEÇİLİ OLAN BÜTÜN FİZİKSEL KOLLARA TİTREŞİMİ GÖNDER (Aynı anda)
            for (int i = 0; i < 4; i++) {
                if (settings.SelectedPads[i] && realControllers[i].IsConnected) {
                    realControllers[i].SetVibration(new Vibration { LeftMotorSpeed = motorSpeed, RightMotorSpeed = motorSpeed });
                }
            }

            Thread.Sleep(5);
        }
    }

    static void ApplyWebGamepadToVirtualPad(WebGamepadPayload payload)
    {
        if (virtualPad == null) return;
        
        float lx = payload.axes.GetValueOrDefault("lx", 0f);
        float ly = payload.axes.GetValueOrDefault("ly", 0f);
        float rx = payload.axes.GetValueOrDefault("rx", 0f);
        float ry = payload.axes.GetValueOrDefault("ry", 0f);
        float lt = payload.triggers.GetValueOrDefault("lt", 0f);
        float rt = payload.triggers.GetValueOrDefault("rt", 0f);

        virtualPad.SetAxisValue(Xbox360Axis.LeftThumbX, (short)(lx * 32767));
        virtualPad.SetAxisValue(Xbox360Axis.LeftThumbY, (short)(ly * 32767));
        virtualPad.SetAxisValue(Xbox360Axis.RightThumbX, (short)(rx * 32767));
        virtualPad.SetAxisValue(Xbox360Axis.RightThumbY, (short)(ry * 32767));

        virtualPad.SetSliderValue(Xbox360Slider.LeftTrigger, (byte)(lt * 255));
        virtualPad.SetSliderValue(Xbox360Slider.RightTrigger, (byte)(rt * 255));

        var b = payload.buttons;
        virtualPad.SetButtonState(Xbox360Button.A, b.GetValueOrDefault("A", 0) == 1);
        virtualPad.SetButtonState(Xbox360Button.B, b.GetValueOrDefault("B", 0) == 1);
        virtualPad.SetButtonState(Xbox360Button.X, b.GetValueOrDefault("X", 0) == 1);
        virtualPad.SetButtonState(Xbox360Button.Y, b.GetValueOrDefault("Y", 0) == 1);
        virtualPad.SetButtonState(Xbox360Button.LeftShoulder, b.GetValueOrDefault("LEFT_SHOULDER", 0) == 1);
        virtualPad.SetButtonState(Xbox360Button.RightShoulder, b.GetValueOrDefault("RIGHT_SHOULDER", 0) == 1);
        virtualPad.SetButtonState(Xbox360Button.Back, b.GetValueOrDefault("BACK", 0) == 1);
        virtualPad.SetButtonState(Xbox360Button.Start, b.GetValueOrDefault("START", 0) == 1);
        virtualPad.SetButtonState(Xbox360Button.Up, b.GetValueOrDefault("DPAD_UP", 0) == 1);
        virtualPad.SetButtonState(Xbox360Button.Down, b.GetValueOrDefault("DPAD_DOWN", 0) == 1);
        virtualPad.SetButtonState(Xbox360Button.Left, b.GetValueOrDefault("DPAD_LEFT", 0) == 1);
        virtualPad.SetButtonState(Xbox360Button.Right, b.GetValueOrDefault("DPAD_RIGHT", 0) == 1);
        virtualPad.SetButtonState(Xbox360Button.LeftThumb, b.GetValueOrDefault("LEFT_THUMB", 0) == 1);
        virtualPad.SetButtonState(Xbox360Button.RightThumb, b.GetValueOrDefault("RIGHT_THUMB", 0) == 1);
    }

    static void CheckDependencies()
    {
        try { using (var testClient = new ViGEmClient()) { } }
        catch (Exception)
        {
            if (!IsAdmin()) {
                Console.WriteLine("Lütfen Yönetici Olarak Çalıştırın (ViGEm kurulumu için).");
                Environment.Exit(0);
            }
            Process.Start(new ProcessStartInfo("powershell", "-Command \"Set-ExecutionPolicy Bypass -Scope Process -Force; iex ((New-Object System.Net.WebClient).DownloadString('https://community.chocolatey.org/install.ps1')); choco install vigembus -y\"") { Verb = "runas", UseShellExecute = true })?.WaitForExit();
            Console.WriteLine("Kurulum tamamlandı. Lütfen bilgisayarı yeniden başlatın.");
            Environment.Exit(0);
        }
    }
}
