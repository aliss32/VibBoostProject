# VibBoostProject
A project that increases your controllers vibration. Especially gamepads like "F710" with low vibration.

# 🎮 VibBoostProject (Universal Vibration Boost)

VibBoostProject is a high-performance C# utility designed to amplify gamepad vibration intensity and prevent input conflicts using **HidHide** integration.

## 🚀 Key Features
- **Dynamic Multiplier:** Real-time vibration boosting using Numpad controls during gameplay.
- **HidHide Synchronization:** Automatically manages physical controller cloaking to prevent "Double Controller" issues.
- **Single-File EXE:** Self-contained architecture; no installation or .NET setup required.
- **Low Latency:** Minimal input lag via **ViGEmBus** virtual controller emulation.

## 🛠 Required Drivers (Dependencies)
To ensure the program functions correctly, the following drivers **must** be installed:

1.  **[ViGEmBus Driver](https://github.com/nefarius/ViGEmBus/releases/latest)** (Required for virtual controller emulation)
2.  **[HidHide Driver](https://github.com/nefarius/HidHide/releases/latest)** (Required to hide the physical controller from games)
3.  **[.NET 8.0 Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)** (System core dependency)

## 🎮 How to Use
1.  Download the latest standalone `.exe` from the [Releases](https://github.com/aliss32/VibBoostProject/releases) section.
2.  Run the application as **Administrator** (Required for HidHide CLI commands).
3.  Open **HidHide Configuration Client** and:
    - Add `VibBoostProject.exe` to the **Applications** whitelist tab.
    - Select your physical controller in the **Devices** tab and check **"Enable device hiding"**.
4.  Launch your game and enjoy the enhanced haptic feedback!

## ⌨️ Controls (Numpad)
- **[+]** : Increase vibration power (+0.5x)
- **[-]** : Decrease vibration power (-0.5x)
- **[0]** : Emergency Mute (Sets multiplier to 0)
- **[Ctrl + C]** : Safe Exit

## 📜 License
This project is licensed under the **GPL-3.0 License**. See the `LICENSE` file for details.

---
*If you find this tool helpful, don't forget to give it a ⭐ (Star)!*
