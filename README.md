# IoTCoWork

> 开源本地工作台外壳。云端思考、本地干活、边缘执行体系中的"本地干活"层。

## 项目定位

IoTCoWork 是 IoTSharp 生态中的本地开发与运维工作台，基于 .NET / Blazor Hybrid 构建。它在用户本机提供：

- 本地建模、调试、生成、发布的统一入口
- 设备/协议/点位等工程模型的本地编辑器
- 本地调试控制台、协议联调、串口/TCP/MQTT/Modbus 实验室
- 与 IoTSharp 平台、IoTEdge 系列基座的桥接

> 本仓库只承载**开源外壳**与插件 SDK。多租户、Copilot 编排、付费模板、企业交付流程等商业能力位于上层 `IoTSharp.SaaS` 仓库的 `src/IoTCoWork.*` 模块内，通过插件接口叠加。

## 当前骨架

当前已经接入 NativeWebHost 桌面宿主，采用 Windows Win32 Runtime + Native WebView2 打开本机 ASP.NET Core 服务；界面由 `IoTCoWork.Workbench` 的 Blazor WebAssembly 客户端在 WebView 内本地渲染。

```text
IoTCoWork.App        # NativeWebHost 桌面宿主、本地 ASP.NET Core API、WASM 静态资源服务
IoTCoWork.Workbench  # Blazor WebAssembly 本地客户端
external/NativeWebHost  # NativeWebHost git 子模块源码依赖
```

开发命令：

```powershell
git submodule update --init --recursive
dotnet build IoTCoWork.sln -m:1

# 启动桌面宿主，默认打开 NativeWebHost 窗口
dotnet run --project IoTCoWork.App -f net10.0-windows

# 只启动本地站点，便于 API / 静态资源冒烟
dotnet run --project IoTCoWork.App -f net10.0 -- --headless --urls http://127.0.0.1:5186
```

VS Code 调试可直接使用 `IoTCoWork: 一键运行桌面版`。该配置会像 Cosmos 一样先把 Workbench 发布到 `artifacts/debug-web`，再启动 `net10.0-windows` 桌面宿主，并把物理静态资源目录传给宿主。

## 与 IoTSharp 生态的关系

```
┌──────────────────────────────────────────────┐
│ IoTSharp.SaaS（商业叠加：Copilot/计费/审计） │  src/IoTCoWork.*
└──────────────────────────────────────────────┘
                   ▲ 插件 SDK
┌──────────────────────────────────────────────┐
│ IoTCoWork（本仓库：开源工作台外壳）            │  ← you are here
└──────────────────────────────────────────────┘
       │           │              │
       ▼           ▼              ▼
   IoTSharp    IoTEdge   IoTEdge.Linux/Stm32
   开源平台     C# AOT 基座     C / MCU 基座
```

## 路线图

详见 [ROADMAP.md](ROADMAP.md)。

## AI 工作台草案

参考 Cowork 的任务工作流模式，已经开始整理一版 IoTCoWork 的 AI 工作台架构草案： [docs/ai-workbench-architecture-draft.md](docs/ai-workbench-architecture-draft.md)

继续拆分后的可执行稿：

- [信息架构](docs/ai-workbench-information-architecture.md)
- [组件映射](docs/ai-workbench-component-mapping.md)
- [插件与工具契约](docs/ai-workbench-plugin-tool-contract.md)
- [物联网业务方案蓝图](docs/iot-workspace-business-model.md)
- [物联网业务方案字段表](docs/iot-workspace-business-profile-fields.md)
- [物联网业务方案 JSON Schema](schemas/iot-workspace-business-profile.schema.json)

## 许可证

Apache-2.0（待补 LICENSE 文件）。
