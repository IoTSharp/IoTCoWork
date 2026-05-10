# IoTClaw AI 工作台组件映射草案 v0.1

> 目标：尽量使用 Ant Design Blazor 和 Ant Design X Blazor 的现成组件，减少自绘 UI。

## 1. 总体策略

- 业务工作台用 `AntDesign Blazor`
- AI 交互区用 `AntDesignXBlazor`
- 缺能力先查组件库，确实不够再改子模块源码
- 主工程里不长期堆原生替代实现

## 2. 工作台骨架

建议映射如下：

| 区域 | 组件 |
| --- | --- |
| 外层布局 | `Layout`, `Header`, `Sider`, `Content`, `Footer` |
| 导航 | `Menu`, `Breadcrumb`, `Dropdown`, `Tabs` |
| 页面标题 | `Typography`, `Space`, `Badge`, `Tag` |
| 概览信息 | `Descriptions`, `Statistic`, `Alert`, `Timeline` |

布局优先采用官方 `Layout` 和 `Menu` 的组合，而不是手搓侧栏。

## 3. 数据工作区

建议映射如下：

| 场景 | 组件 |
| --- | --- |
| 项目/设备树 | `Tree` |
| 列表与筛选 | `Table`, `Pagination`, `Input.Search`, `Select` |
| 配置编辑 | `Form`, `Input`, `TextArea`, `Switch`, `Upload` |
| 详情查看 | `Descriptions`, `Card`, `Tabs`, `Drawer` |
| 风险确认 | `Modal`, `Popconfirm`, `Result` |
| 运行监控 | `Steps`, `Timeline`, `Progress`, `Spin`, `Empty` |
| 业务方案向导 | `Steps`, `Form`, `Select`, `TreeSelect`, `Table`, `Drawer` |

页面 section 尽量做成清晰的工作带，不要把整个页面包成一层层浮卡。

业务方案向导至少要覆盖：

- 数据接收终点
- 采集器 / 网关
- 来源协议
- 转换规则
- 上传通道

## 4. AI 交互区

建议映射如下：

| 场景 | 组件 |
| --- | --- |
| 会话列表 | `Conversations` |
| 消息展示 | `Bubble` |
| 欢迎引导 | `Welcome` |
| 快速提示 | `Prompts` |
| 输入与提交 | `Sender` |
| 附件上传 | `Attachment` |
| 快捷动作 | `Suggestion` |
| 审核思路 | `ThoughtChain` |
| 模型请求 | `XRequest` + `XAgent` 服务适配 |
| 流式输出 | `XStream` + `XChat` 服务适配 |

建议把 `Bubble` 用来展示结果流，把 `ThoughtChain` 用来展示过程流。

## 5. 组件职责边界

### AntDesign Blazor

负责：

- 结构布局
- 表格和表单
- 树与列表
- 弹窗和抽屉
- 状态标签和通知

### AntDesignXBlazor

负责：

- 会话与消息
- 提示词入口
- 任务建议
- 过程展示
- 流式结果

其中 `XRequest` 和 `XStream` 更适合放在我们的内部契约层，`XAgent` / `XChat` 再作为实现适配层。

### 适配层

如果组件 API 和实际需求不完全一致，建议在子模块里补齐，而不是在主工程里反复写临时适配。

## 6. 优先 PoC

建议先做四个最小闭环：

1. 左侧项目树 + 中间任务表 + 右侧 AI 面
2. `Sender` 提交一个任务草案
3. `Bubble` 流式显示执行结果
4. `Drawer` 打开任务详情和工具回显

## 7. 兼容性注意

当前主工程是 `net10.0`，而 Ant Design X 官方文档当前主要展示 .NET 8 方案。
因此第一阶段必须先做兼容性验证，再决定是否需要补子模块源码。

## 8. 参考

- Ant Design Layout: https://antblazor.com/en-US/components/layout
- Ant Design Menu: https://antblazor.com/en-US/components/menu
- Ant Design Form: https://antblazor.com/en-US/components/form
- Ant Design Table: https://antblazor.com/en-US/components/table
- Ant Design Descriptions: https://antblazor.com/en-US/components/descriptions
- Ant Design X overview: https://x.antblazor.com/en-US/components/overview
- Ant Design X intro: https://x.antblazor.com/en-US/docs/introduce
- Bubble: https://x.antblazor.com/en-US/components/bubble
- Conversations: https://x.antblazor.com/en-US/components/conversations
- Sender: https://x.antblazor.com/en-US/components/sender
