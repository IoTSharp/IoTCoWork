# IoTCoWork AI 工作台信息架构草案 v0.1

> 目标：把 Cowork 的“项目 - 任务 - 工具 - 记忆”组织方式，翻译成 IoTCoWork 的工作台信息结构。

## 1. 首屏原则

首屏不是聊天页，而是工作区总览。

用户进入后应先看到：

- 当前 workspace
- 业务方案状态
- 进行中的任务
- 资产、点位、协议绑定、工艺关系建模状态
- 最近产物和告警
- 可直接发起的 AI 动作

## 2. 全局层级

建议的层级顺序是：

1. Workspace
2. Project
3. Task
4. Tool Run
5. Artifact

每一级都要能独立查看，也要能向下钻取。

## 3. 主导航

建议左侧固定导航包含：

- 总览
- 业务方案
- 项目
- 任务
- 设备
- 协议
- 边缘
- 产物
- 日志
- 设置

如果后续插件很多，再加二级分组，但不要先把导航做碎。

## 4. 页面结构

### 4.1 总览页

总览页应该聚合：

- workspace 状态
- 最近任务
- 运行摘要
- 连接器健康
- AI 建议任务

### 4.2 项目页

项目页应该围绕“一个交付目标”组织：

- 项目树
- 业务方案
- 模型文件
- 设备清单
- 协议清单
- 下发配置
- 历史记录

### 4.3 任务页

任务页是核心执行面：

- 任务目标
- 计划步骤
- 使用工具
- 审批节点
- 执行结果
- 关联产物

### 4.4 AI 页

AI 页不是独立的聊天区，而是工作流的右侧操作面：

- 会话列表
- 当前会话
- 计划与分解
- 快捷提示
- 工具回显
- 思维链与审批

### 4.5 业务方案页

语义建模页是 workspace 的必填入口，应该编辑：

- L2 资产结构：site、area、line、device、component 与 `assetPath`
- L1 点位语义：`semanticId`、单位、量纲、访问权限、质量、正常范围
- Protocol Binding：Modbus register、MQTT Topic、OPC UA NodeId 等来源映射
- L3 工艺关系：上下游、派生变量、状态、报警与控制策略
- 生成目标：C# AOT、IoTEmbedded、BASIC 等后续输出占位

点表、Topic 样例和 NodeSet 可作为导入来源，但不能再作为内部主模型。

## 5. 交互流

标准流程建议是：

1. 选择 workspace 或 project
2. 由 AI 生成任务草案
3. 人工确认或修改计划
4. AI 调用工具执行
5. 结果回写到 task 和 artifact
6. 进入归档或继续迭代

## 6. 状态模型

页面与任务至少要覆盖这些状态：

- 空白
- 加载中
- 已连接
- 未连接
- 运行中
- 待确认
- 成功
- 失败
- 部分成功

## 7. 视觉优先级

建议遵循这些规则：

- 任务状态比装饰更重要
- 执行结果比说明文字更重要
- 风险提示比普通摘要更显眼
- 产物列表比营销式内容更靠前

## 8. 反模式

不要先做这些东西：

- 纯聊天首页
- 大面积空白英雄区
- 把任务细节藏在多层弹窗里
- 用卡片堆出整页
- 让 AI 面和业务面完全割裂

## 9. 下一步接法

这个信息架构会直接喂给：

- [组件映射](ai-workbench-component-mapping.md)
- [插件与工具契约](ai-workbench-plugin-tool-contract.md)
- [语义建模信息架构](semantic-modeling-information-architecture.md)
- [业务方案蓝图](iot-workspace-business-model.md)
- [业务方案字段表](iot-workspace-business-profile-fields.md)
- [业务方案 JSON Schema](../schemas/iot-workspace-business-profile.schema.json)

## 10. 参考

- Cowork: https://claude.com/product/cowork
- Projects: https://support.claude.com/en/articles/14116274-organize-your-tasks-with-projects-in-claude-cowork
- Plugins: https://support.claude.com/en/articles/13837440-use-plugins-in-claude-cowork
