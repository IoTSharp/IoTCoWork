# IoTCoWork AI 工作台插件与工具契约草案 v0.1

> 目标：把设备、协议、边缘、分析和 AI 动作统一成可注册、可审计、可扩展的契约。

## 1. 原则

- 所有真实动作都必须经过显式工具边界
- 高风险动作必须要求确认
- 插件只描述能力，不直接把商业逻辑塞进开源壳
- 工具调用必须可追踪、可回放、可归档

## 2. 核心对象

建议先定这几个概念：

- `Workspace`
- `Project`
- `Plugin`
- `Tool`
- `Command`
- `Connector`
- `DataReceiver`
- `Collector`
- `SourceSpec`
- `CodecRule`
- `UplinkRoute`
- `Run`
- `Artifact`
- `Policy`
- `Approval`

其中 `DataReceiver`、`Collector`、`SourceSpec`、`CodecRule`、`UplinkRoute` 组成 workspace 的业务方案五要素。

### 2.1 业务方案对象

业务方案对象用于描述完整数据链路：

- `DataReceiver`：IoTSharp、ThingsBoard、阿里云 IoT 平台、时序数据库或业务应用接口
- `Collector`：自研采集器、IoTCoWork Edge、第三方网关或设备直连代码
- `SourceSpec`：来源类型、物理链路、协议、地址和点位表
- `CodecRule`：字节序、Word 顺序、缩放、偏移、枚举和公式
- `UplinkRoute`：MQTT、HTTP、数据库写入或其他上传方式

## 3. 插件清单

建议每个插件至少包含这些字段：

- id
- name
- version
- description
- category
- permissions
- tools
- commands
- views
- connectors
- dependencies

### 推荐 category

- protocol
- edge
- analysis
- integration
- ui
- template

## 4. 工具契约

每个工具建议包含：

- id
- title
- description
- inputSchema
- outputSchema
- riskLevel
- approvalMode
- sideEffects
- timeout
- retryable

### 推荐 riskLevel

- low
- medium
- high

### 推荐 approvalMode

- never
- once
- perRun
- manual

## 5. 连接器契约

连接器用于把外部系统接进工作台，建议描述：

- transport
- endpoint
- auth
- healthCheck
- telemetryMapping
- readScope
- writeScope

典型连接器包括：

- MQTT
- Modbus
- OPC UA
- HTTP API
- 文件系统
- 本地数据库
- 边缘节点 API

## 6. 任务与运行

任务应独立于工具存在，运行只是任务的一个实例。

建议状态：

- Draft
- Planned
- Ready
- Running
- WaitingApproval
- Succeeded
- Failed
- Archived

一个 `Run` 至少要记录：

- taskId
- workspaceId
- pluginId
- toolId
- actor
- startedAt
- endedAt
- result
- artifactRefs
- traceId

## 7. 菜单与视图贡献

插件建议支持这些贡献点：

- 菜单项
- 命令面板
- 页面视图
- 侧边栏面板
- 详情抽屉
- 工具条按钮

这样插件就能自然嵌入工作台，而不是挂在一个孤立入口里。

## 8. 权限模型

建议先把权限拆成清晰的 scope：

- file.read
- file.write
- project.read
- project.write
- device.read
- device.write
- connector.read
- connector.write
- edge.deploy
- telemetry.read
- model.call

高风险 scope 应该默认关闭，等用户或策略放行。

## 9. 审计要求

每次工具执行都要记录：

- 谁发起的
- 依据是什么
- 输入是什么
- 结果是什么
- 是否需要人工确认
- 是否生成了产物

## 10. 与后续实现的关系

这个契约会直接影响：

- 插件 SDK
- 边缘发布流程
- 协议调试器
- AI 任务调度
- 审批与回放

## 11. 参考

- Cowork plugins: https://support.claude.com/en/articles/13837440-use-plugins-in-claude-cowork
- Cowork safety: https://support.claude.com/en/articles/13364135-use-cowork-safely
- Ant Design X overview: https://x.antblazor.com/en-US/components/overview
