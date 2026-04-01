# 🎮 VibBoost V1.0.4 (Haptic Live Dashboard)
**Developed by: alissgmr**

VibBoost is an advanced, high-performance C# utility designed to transform your gaming experience. Unlike standard boosters, it uses a **Non-Linear (Logarithmic) Power Curve** to convert system audio and game feedback into realistic, organic haptic vibrations. 

> **Note:** Especially effective for gamepads like "Logitech F710" or controllers with naturally low vibration intensity.
---
<img width="1347" height="685" alt="image" src="https://github.com/user-attachments/assets/970d95a2-dbda-42c1-a266-fb56e70bdc3e" />


---

## 🚀 Key Features
* **Hybrid Mixer Engine:** Simultaneously merges Windows Audio Peaks with real-time Game Feedback.
* **Auto-Dependency Installer:** Automatically detects, downloads, and installs **ViGEmBus** and **Chocolatey** if missing (Requires Admin).
* **Smart Smoothing (Organic Feel):** Features an **Attack/Release algorithm** to prevent "mechanical" vibrations and provide a "tok" (premium) haptic feel.
* **Only-Bass Mode:** Toggle **REAL DSP BASS ONLY** Mode on - off for better experience.
* **Live Dashboard UI:** A flicker-free console interface with real-time audio input and vibration output visualizers.
* **Zero-Setup Architecture:** Single-file EXE with all libraries (NAudio, ViGEm, SharpDX) embedded. No .NET installation required.

---

## 🛠️ Intelligent Setup (Auto-Healing)
You no longer need to manually install drivers. The program handles the heavy lifting:

1.  **Run `VibBoost.exe` as Administrator.**
2.  The program will automatically scan your system for the **ViGEmBus** driver.
3.  If missing, it will initiate a safe, automated installation via **Chocolatey**.
4.  ⚠️ **Important:** A system restart is required **only once** after the initial driver installation to activate the virtual bus.

---

## ⌨️ Controls (Numpad)
| Key | Action |
| :--- | :--- |
| **(NUM) [+] / [-]** | Adjust **Minimum Vibration Power** (Base Intensity) |
| **(NUM) [*] / [/]** | Adjust **Noise Gate** (Filters out background static/hiss) |
| **[Del]** | **Toggle Smoothing** (Switch between Organic/Soft and Raw/Sharp) |
| **(NUM) [0]** | Toggle  **REAL DSP BASS ONLY** Mode  |
| **[Ctrl + C]** | Safe Exit |

---

## 📊 Live Monitoring
The dashboard provides two real-time, high-precision progress bars:
* **AUDIO IN:** Shows exactly how much sound the engine is picking up from your system.
* **VIB OUT:** Shows the final, calculated intensity being sent to your controller's motors.

---

## 📜 Requirements
- **OS:** Windows 10/11 (64-bit)
- **Hardware:** Xbox Compatible Controller (Physical or Emulated)
- **Network:** Internet Connection (Only required for the first run to auto-install drivers)

---
*If you find this tool helpful, don't forget to give it a ⭐ (Star) on GitHub!*
---
*Note: This documentation was polished with the help of AI to provide clear English instructions, but the project is independently developed by alissgmr.*
