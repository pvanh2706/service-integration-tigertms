# Chạy dưới dạng Windows Service

## 1) Publish
```powershell
cd src/ServiceIntegrationDemo
dotnet publish -c Release -r win-x64 --self-contained true -o publish
```

## 2) Cài service (PowerShell chạy as Administrator)
```powershell
sc.exe create ServiceIntegrationDemo binPath= "C:\path\to\publish\ServiceIntegrationDemo.exe" start= auto
sc.exe start ServiceIntegrationDemo
```

## 3) Uninstall
```powershell
sc.exe stop ServiceIntegrationDemo
sc.exe delete ServiceIntegrationDemo
```
