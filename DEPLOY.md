Development PC: run next lines into powershell

dotnet publish D:\_TCPOS\Plugins\git-hub\KaAPI\KaApi.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -o d:\Deploy\KaApi
  
  
Client PC run powershell

New-Service `
  -Name "KaApi" `
  -BinaryPathName '"C:\KAMI SL\KaAPIService\KaApi.exe"' `
  -DisplayName "KaApi Sales Import" `
  -StartupType Automatic

Start-Service KaApi 