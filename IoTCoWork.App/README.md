# IoTCoWork.App

IoTCoWork 本地工作台外壳。当前使用 NativeWebHost 的 Win32 Runtime + Native WebView2 作为桌面宿主，启动本机 ASP.NET Core 服务并承载 `IoTCoWork.Workbench` 的 Blazor WebAssembly 客户端。

```powershell
# 启动桌面程序窗口
dotnet run --project IoTCoWork.App -f net10.0-windows

# 只启动本地站点，便于 API / 静态资源冒烟
dotnet run --project IoTCoWork.App -f net10.0 -- --headless --urls http://127.0.0.1:5186
```
