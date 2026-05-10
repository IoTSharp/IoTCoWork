# IoTCoWork.App

IoTCoWork 本地工作台外壳。当前使用 OmniHost 的 Win32 Runtime + Native WebView2 作为桌面宿主，启动本机 ASP.NET Core 服务并承载 `IoTCoWork.Workbench` 的 Blazor WebAssembly 客户端。

```powershell
dotnet run --project IoTCoWork.App
dotnet run --project IoTCoWork.App -- --headless --urls http://127.0.0.1:5186
```
