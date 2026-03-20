# VibBoostProject
A Tool that increases your controllers vibration with AUDIO input. Especially gamepads like "F710" with low vibration.

🎮 VibBoost V1.0.0 (Haptic Live Dashboard)

VIB-BOOST is an advanced, high-performance C# utility designed to transform your gaming experience. Unlike standard boosters, it uses a Non-Linear (Logarithmic) Power Curve to convert system audio and game feedback into realistic, organic haptic vibrations.

🚀 Key Features
Hybrid Mixer Engine: Simultaneously merges Windows Audio Peaks with real-time Game Feedback.

Auto-Dependency Installer: Automatically detects, downloads, and installs ViGEmBus and Chocolatey if missing (Requires Admin).

Smart Smoothing (Organic Feel): Features an Attack/Release algorithm to prevent "mechanical" vibrations and provide a "tok" (premium) haptic feel.

Live Dashboard UI: A flicker-free console interface with real-time audio input and vibration output visualizers.

Zero-Setup Architecture: Single-file EXE with all libraries (NAudio, ViGEm, SharpDX) embedded. No .NET installation required.

🛠️ Intelligent Setup (Auto-Healing)
You no longer need to manually install drivers.

Run VibBoost.exe as Administrator.

The program will scan your system for ViGEmBus.

If missing, it will automatically initiate a safe installation via Chocolatey.

Note: A system restart is required only once after the initial driver installation.

⌨️ Controls (Numpad)
(NUM) [+] / [-] : Adjust Minimum Vibration Power (Base Intensity).

(NUM) [*] / [/] : Adjust Noise Gate (Filters out background static/hiss).

(Del) : Toggle Smoothing (Switch between Organic/Soft and Raw/Sharp vibration).

[Ctrl + C] : Safe Exit.

📊 Live Monitoring
The dashboard provides two real-time progress bars:

AUDIO IN: Shows exactly how much sound the engine is picking up.

VIB OUT: Shows the final intensity being sent to your controller's motors.

📜 Requirements
Windows 10/11 (64-bit)

Xbox Compatible Controller (Physical or Emulated)

Internet Connection (Only for the first run to auto-install drivers)

*If you find this tool helpful, don't forget to give it a ⭐ (Star)!*
