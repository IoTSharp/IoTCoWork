# IoTCoWork AI 工作台架构草案 v0.1

> 目标：参考 Claude Cowork 的工作方式，构建 IoTCoWork 的本地 AI 工作台，用于控制 IoTSharp、边缘模块和本地工程资产，形成“分析 - 规划 - 执行 - 归档”的闭环。

## 1. 草案结论

IoTCoWork 不应做成单纯聊天窗口，而应做成“任务工作台”。

推荐架构是：

- `IoTCoWork.App` 继续承担本地宿主、API、静态资源和桌面启动。
- `IoTCoWork.Workbench` 作为统一前端工作台。
- `AntDesign Blazor` 负责全部业务型工作台界面。
- `AntDesignXBlazor` 负责 AI 交互面、会话面、提示词面和思维链面。
- `IoTCoWork.PluginSdk` 负责把设备、协议、边缘、分析能力抽象成可扩展插件。

这条路线的核心原则是：

1. 先统一工作流，再统一视觉。
2. 能用组件库就不用原生拼装。
3. 子模块如果不满足需求，直接改子模块源码，不在主工程里散写替代实现。
4. AI 负责分析和编排，真实动作必须经过明确工具边界。

## 2. 从 Cowork 学什么

Cowork 的关键不是聊天，而是把任务组织成可执行系统。

我们要吸收的模式有：

- 项目化：每个工作区围绕一个目标收敛上下文。
- 工具化：能力通过工具、插件、连接器显式暴露。
- 分解式：大任务先拆成小任务，再并行或串行执行。
- 可审计：所有动作能回看、能追踪、能确认。
- 本地优先：敏感文件和操作尽量留在本机工作区。

对 IoTCoWork 来说，对应关系是：

- Project -> 站点、产线、设备群或交付包。
- Task -> 采集、建模、调试、下发、诊断、分析。
- Plugin -> 协议包、边缘包、数据处理包、报表包。
- Connector -> MQTT、Modbus、OPC UA、HTTP、数据库、文件。
- Memory -> 仅限当前 workspace 的任务记忆和决策记录。

## 3. 产品分层

### 3.1 宿主层

`IoTCoWork.App` 负责：

- 启动桌面宿主
- 托管本机 ASP.NET Core 服务
- 提供 WebView2 / OmniHost 入口
- 提供本地 API、健康检查、宿主信息

这一层只管“运行起来”，不承载业务 UI 逻辑。

### 3.2 工作台层

`IoTCoWork.Workbench` 负责：

- 左侧导航
- 顶部命令区
- 中央工作区
- AI 任务区
- 数据表、表单、树、抽屉、弹窗、详情页

它是整个 IoTCoWork 的唯一前端入口。

### 3.3 能力层

`IoTCoWork.PluginSdk` 负责：

- 能力注册
- 命令注册
- 视图贡献点
- 菜单贡献点
- 工具调用契约
- 插件生命周期

后续所有协议、设备、分析、边缘动作都通过这个层暴露，不直接散落在 UI 里。

### 3.4 边缘与平台层

IoTSharp 与边缘模块负责：

- 采集
- 缓冲
- 上行
- 规则执行
- 本地推理或预处理

IoTCoWork 负责配置、编排、调试、监控与回放。

## 4. UI 策略

### 4.1 组件选型

主 UI 统一使用 `AntDesign Blazor`。

适合优先落地的组件：

- `Layout`
- `Menu`
- `Tabs`
- `Table`
- `Form`
- `Tree`
- `Card`
- `Modal`
- `Drawer`
- `Steps`
- `Timeline`
- `Alert`
- `Descriptions`

AI 交互统一使用 `AntDesignXBlazor`。

适合优先落地的组件：

- `Conversations`
- `Bubble`
- `Welcome`
- `Prompts`
- `Sender`
- `Attachment`
- `Suggestion`
- `ThoughtChain`
- `XRequest`
- `XStream`

### 4.2 界面结构

建议把工作台拆成四个固定区域：

1. 左侧：项目、能力、连接器、设备树
2. 顶部：全局命令、环境状态、搜索、发布入口
3. 中央：当前任务的主编辑区
4. 右侧：AI 交互区、上下文、计划、诊断结果

这会比“一个聊天框加几个卡片”更接近 Cowork 的实际工作方式。

### 4.3 不做什么

以下内容尽量不写原生实现：

- 原生 HTML 按钮组代替菜单
- 原生 DIV 拼出来的卡片系统
- 自写聊天气泡
- 自写弹窗和抽屉
- 自己造表单校验和表格筛选

缺的只做两件事：

1. 先看组件库有没有现成能力。
2. 没有就改子模块源码，不在主工程里长期堆临时替身。

### 4.4 AI 请求与流式输出

AI 交互层建议把两个能力先作为标准件：

- `XRequest`：统一模型请求、工具调用和上游兼容
- `XStream`：统一流式输出、增量渲染和实时状态更新

这样可以把“回答文本”和“执行过程”拆成两条流：

- 结果流：给用户看最终输出
- 过程流：给用户看计划、思维链和执行状态

## 5. 领域模型

建议先固化这些核心对象：

- `BusinessProfile`
- `Workspace`
- `Project`
- `Task`
- `Agent`
- `Tool`
- `Connector`
- `Artifact`
- `Policy`
- `Memory`
- `Run`
- `Result`

`BusinessProfile` 是 workspace 的业务底座，必须回答：

- 数据上传给谁
- 谁来采集
- 数据从哪里来、怎么采
- 数据如何转换
- 通过什么通道上传

建议先定义这些任务状态：

- `Draft`
- `Planned`
- `Ready`
- `Running`
- `WaitingApproval`
- `Succeeded`
- `Failed`
- `Archived`

建议先定义这些工具边界：

- 读文件
- 写文件
- 列目录
- 调试协议
- 连接设备
- 发布边缘包
- 拉取遥测
- 生成报表
- 写入工程模型

## 6. 交互原则

建议沿用 Cowork 的节奏：

- 先分析，再执行
- 先列计划，再发动作
- 高风险动作必须显式确认
- 每一步都能看到来源、结果和影响面
- 所有产物都要回写到 workspace

这会让 AI 从“聊天助手”变成“工作流编排器”。

## 7. 本地与云端边界

### 本地保留

- 工程文件
- 设备配置
- 协议调试记录
- 采集缓存
- 发布包
- 任务历史

### 云端可接入

- 更强模型推理
- 团队共享模板
- 商业 Copilot 编排
- 审计与计费

本仓库只实现开源外壳，不把租户、License、计费逻辑写进来。

## 8. 子模块策略

明确建议：

- `AntDesignXBlazor` 采用可编辑源码依赖，而不是只挂 NuGet。
- 组件不满足需求时，优先修改子模块源码。
- 主仓库只保留调用和少量适配层。
- 子模块改动必须保留清晰的上游差异说明。

这条规则是为了避免后期被 UI 黑盒卡死。

## 9. 逐步落地顺序

### 第一阶段

- 接入 `AntDesign Blazor`
- 建立 Layout + Menu + Tabs 的工作台骨架
- 引入 AI 侧栏占位
- 建立 Workspace / Project / Task 的 DTO

### 第二阶段

- 接入 `AntDesignXBlazor`
- 做 Conversations + Sender + Bubble
- 做任务计划与审批流
- 做本地任务历史

### 第三阶段

- 接入插件 SDK
- 做协议与设备树
- 做调试台与数据面板
- 做边缘发布和回放

### 第四阶段

- 做模板市场
- 做协作和共享
- 做云端增强能力

## 10. 当前待确认的问题

- `AntDesignXBlazor` 是否以子模块形式纳入仓库
- 是否先做纯本地工作台，还是同步准备 SaaS 插件位
- 任务执行是否需要独立的本地调度器
- 设备和协议的首批 MVP 范围
- 采集、上传、分析三条链路的优先级

## 11. 参考资料

- Claude Cowork: https://claude.com/product/cowork
- Cowork getting started: https://support.claude.com/en/articles/13345190-get-started-with-cowork
- Cowork projects: https://support.claude.com/en/articles/14116274-organize-your-tasks-with-projects-in-claude-cowork
- Cowork plugins: https://support.claude.com/en/articles/13837440-use-plugins-in-claude-cowork
- Cowork safety: https://support.claude.com/en/articles/13364135-use-cowork-safely
- Ant Design Blazor layout: https://antblazor.com/en-US/components/layout
- Ant Design Blazor menu: https://antblazor.com/en-US/components/menu
- Ant Design Blazor form: https://antblazor.com/en-US/components/form
- Ant Design Blazor table: https://antblazor.com/en-US/components/table
- Ant Design X of Blazor: https://x.antblazor.com/en-US/docs/introduce
- Bubble: https://x.antblazor.com/en-US/components/bubble
- Conversations: https://x.antblazor.com/en-US/components/conversations
- Sender: https://x.antblazor.com/en-US/components/sender
- Prompts: https://x.antblazor.com/en-US/components/prompts
- ThoughtChain: https://x.antblazor.com/en-US/components/thoughtchain
- Suggestion: https://x.antblazor.com/en-US/components/suggestion
- XRequest: https://x.antblazor.com/en-US/components/xrequest
- XStream: https://x.antblazor.com/en-US/components/xstream

## 12. 拆分阅读

这份总草案后面会继续拆成三份可执行稿：

- [信息架构](ai-workbench-information-architecture.md)
- [组件映射](ai-workbench-component-mapping.md)
- [插件与工具契约](ai-workbench-plugin-tool-contract.md)
- [物联网业务方案蓝图](iot-workspace-business-model.md)
