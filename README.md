# AttendanceBot

AttendanceBot adalah aplikasi WPF .NET 8 untuk satu operator yang mengelola profil absensi yang telah diizinkan, menjalankan provider GPS dan jaringan secara berurutan, mencatat hasil ke SQLite, dan mengirim notifikasi Telegram.

Provider dibuat fail-safe: URL dan selector website tidak diisi secara default. Tidak ada GPS spoofing, network spoofing, CAPTCHA/selfie/fingerprint bypass, atau pemalsuan hasil. Saat konfigurasi workflow/selector belum tersedia atau verifikasi manusia diperlukan, provider berhenti dengan `MANUAL_REQUIRED`.

## Build

```powershell
dotnet restore AttendanceBot.sln
dotnet build AttendanceBot.sln -c Release
dotnet test AttendanceBot.sln -c Release
dotnet publish src/AttendanceBot.App/AttendanceBot.App.csproj -c Release -r win-x64 --self-contained true -o publish/win-x64
dotnet publish src/AttendanceBot.Worker/AttendanceBot.Worker.csproj -c Release -r win-x64 --self-contained true -o publish/win-x64
```

Compile `installer/AttendanceBot.iss` dengan Inno Setup untuk menghasilkan `installer/Output/AttendanceBot-Setup-v1.0.0.exe`.

## Instalasi Windows 11

Jalankan installer sebagai administrator. Installer memasang aplikasi ke Program Files, membuat shortcut, folder `%ProgramData%\AttendanceBot`, dan task `AttendanceBot Startup` dengan trigger logon pada sesi interaktif. Database, log, screenshot, dan credential terenkripsi dipertahankan saat upgrade. Penghapusan data aplikasi dilakukan manual setelah uninstall bila operator menginginkannya.

## Konfigurasi provider

URL dan selector asli belum diberikan. Isi `provider-config.example.json` melalui konfigurasi aplikasi/provider setelah meninjau workflow website dan izin organisasi. Jangan mengisi selector dengan tebakan. Edge harus terpasang; browser visible default true agar verifikasi manual dapat dilakukan.

## Data dan keamanan

Database berada di `%ProgramData%\AttendanceBot\attendance.db`, log di `%ProgramData%\AttendanceBot\Logs`, screenshot di `%ProgramData%\AttendanceBot\Screenshots\YYYY-MM-DD`. Password dan token dirancang disimpan dengan Windows DPAPI CurrentUser dan tidak ditulis ke audit log atau Telegram.
