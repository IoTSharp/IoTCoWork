# IoTClaw

> 开源本地工作台外壳。云端思考、本地干活、边缘执行体系中的"本地干活"层。

## 项目定位

IoTClaw 是 IoTSharp 生态中的本地开发与运维工作台，基于 .NET / Blazor Hybrid 构建。它在用户本机提供：

- 本地建模、调试、生成、发布的统一入口
- 设备/协议/点位等工程模型的本地编辑器
- 本地调试控制台、协议联调、串口/TCP/MQTT/Modbus 实验室
- 与 IoTSharp 平台、IoTSharp.Edge 系列基座的桥接

> 本仓库只承载**开源外壳**与插件 SDK。多租户、Copilot 编排、付费模板、企业交付流程等商业能力位于上层 `IoTSharp.SaaS` 仓库的 `src/IoTClaw.*` 模块内，通过插件接口叠加。

## 当前骨架

当前已经接入 OmniHost 桌面宿主，采用 Windows Win32 Runtime + Native WebView2 打开本机 ASP.NET Core 服务；界面由 `IoTClaw.Workbench` 的 Blazor WebAssembly 客户端在 WebView 内本地渲染。

```text
IoTClaw.App        # OmniHost 桌面宿主、本地 ASP.NET Core API、WASM 静态资源服务
IoTClaw.Workbench  # Blazor WebAssembly 本地客户端
external/OmniHost  # OmniHost git 子模块源码依赖
```

开发命令：

```powershell
git submodule update --init --recursive
dotnet build IoTClaw.sln -m:1

# 启动桌面宿主，默认打开 OmniHost 窗口
dotnet run --project IoTClaw.App

# 只启动本地站点，便于 API / 静态资源冒烟
dotnet run --project IoTClaw.App -- --headless --urls http://127.0.0.1:5186
```

## 与 IoTSharp 生态的关系

```
┌──────────────────────────────────────────────┐
│ IoTSharp.SaaS（商业叠加：Copilot/计费/审计） │  src/IoTClaw.*
└──────────────────────────────────────────────┘
                   ▲ 插件 SDK
┌──────────────────────────────────────────────┐
│ IoTClaw（本仓库：开源工作台外壳）            │  ← you are here
└──────────────────────────────────────────────┘
       │           │              │
       ▼           ▼              ▼
   IoTSharp    IoTSharp.Edge   IoTSharp.Edge.Linux/Stm32
   开源平台     C# AOT 基座     C / MCU 基座
```

## 路线图

详见 [ROADMAP.md](ROADMAP.md)。

## 许可证

Apache-2.0（待补 LICENSE 文件）。
