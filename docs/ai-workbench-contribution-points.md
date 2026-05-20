# AI 工作台插件贡献点

本文档定义 IoTCoWork 开源工作台外壳的最小 UI 贡献点。贡献点只描述本地 UI 元数据和动作占位，不包含租户、计费、License、付费模板、云端 Copilot 编排或业务遥测字段。

## 设计边界

- `external/IoTCoWork` 只提供开源接口、内置占位贡献和 UI 插槽。
- 设备管理、遥测、属性、事件、告警入口只能跳转用户自有 IoTSharp 实例，不在工作台外壳实现 API。
- 商业插件只能由上层仓库注入，例如 `IoTSharp.SaaS/src/*` 或上层宿主项目注册额外 `IWorkbenchContributionProvider`。
- 贡献点应保持 extend-only：新增字段优先使用可选属性或新增描述符类型，不修改已发布构造参数语义。

## 最小接口

工作台通过 `IWorkbenchContributionProvider` 收集贡献：

```csharp
public interface IWorkbenchContributionProvider
{
    IEnumerable<WorkbenchContribution> GetContributions(WorkbenchContributionContext context);
}
```

`WorkbenchContributionContext` 只包含本地显示上下文：workspace 名称、当前会话、模式、状态、模型、目标尺寸、产物数量和运行状态。

## 贡献点类型

| 类型 | UI 区域 | 关键字段 |
| --- | --- | --- |
| `NavigationContribution` | 左侧导航 | `Id`、`Title`、`Description`、`Icon`、`ActionId` |
| `ContextTabContribution` | 右侧上下文 Tabs | `Id`、`Title`、`Description`、`Icon`、`Badge`、`Kind` |
| `SettingsCategoryContribution` | 设置中心分类 | `Id`、`Title`、`Description`、`Icon`、`Keywords`、`Status` |
| `SenderContextChipContribution` | 输入器上下文芯片 | `Id`、`Title`、`Description`、`Icon`、`Value`、`ActionId`、`IsStatic` |

当前开源外壳内置 provider 是 `BuiltinWorkbenchContributionProvider`。外部 provider 可以追加贡献项；未知 `ContextTab` 与设置分类会显示只读占位，具体渲染由上层宿主或插件提供。

## 禁止字段

贡献描述符不得新增以下字段：

- tenant、subscription、billing、wallet、balance、quota、invoice
- license、enterprise license、paid template、pricing
- device telemetry、business telemetry、device command、firmware control
- customer PII、生产密钥、访问令牌、连接串

如商业插件需要这些能力，应在上层闭源仓库实现并通过公开 UI 插槽注入，不得修改 `external/IoTCoWork` 的开源属性。
