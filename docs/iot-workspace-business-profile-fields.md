# IoTClaw 物联网业务方案字段表 v0.1

> 目标：把 workspace 的物联网业务方案拆成可直接喂给表单、持久化和 AI 生成器的字段清单。

## 1. 顶层字段

| 字段路径 | 类型 | 必填 | 说明 | 示例 |
| --- | --- | --- | --- | --- |
| `schemaVersion` | string | 是 | 方案版本，便于后续迁移。 | `0.1` |
| `workspaceId` | string | 是 | 工作空间标识。 | `ws-factory-a` |
| `workspaceName` | string | 是 | 工作空间名称。 | `一号工厂能耗采集` |
| `displayName` | string | 否 | 对外展示名。 | `Factory A Energy` |
| `description` | string | 否 | 业务说明。 | `采集车间电表并上传到 ThingsBoard` |
| `tags` | string[] | 否 | 业务标签。 | `["factory", "energy"]` |
| `receiver` | object | 是 | 数据接收终点。 | 见下文 |
| `collector` | object | 是 | 数据采集器 / 网关。 | 见下文 |
| `source` | object | 是 | 数据来源与采集方式。 | 见下文 |
| `codec` | object | 是 | 转换与解码规则。 | 见下文 |
| `uplink` | object | 是 | 数据上传方式。 | 见下文 |
| `governance` | object | 否 | 审批、审计、风险策略。 | 见下文 |
| `notes` | string | 否 | 备注。 | `夜班模式单独上传` |

## 2. `receiver`

| 字段路径 | 类型 | 必填 | 说明 | 示例 |
| --- | --- | --- | --- | --- |
| `receiver.targetType` | enum | 是 | 接收目标类型。 | `ThingsBoard` |
| `receiver.vendor` | string | 否 | 厂商或产品名。 | `Alibaba Cloud` |
| `receiver.instanceId` | string | 否 | 实例或租户标识。 | `tb-prod-01` |
| `receiver.tenantOrProject` | string | 否 | 租户或项目。 | `iot-demo` |
| `receiver.endpoint` | string | 是 | 接收端地址。 | `https://tb.example.com` |
| `receiver.authMode` | enum | 是 | 鉴权方式。 | `AccessToken` |
| `receiver.credentialRef` | string | 否 | 凭据引用。 | `secret://tb-token` |
| `receiver.protocol` | enum | 是 | 数据接收协议。 | `MQTT` |
| `receiver.payloadFormat` | enum | 是 | 负载格式。 | `JSON` |
| `receiver.topicOrPath` | string | 否 | Topic 或路径。 | `v1/devices/me/telemetry` |
| `receiver.retentionPolicy` | string | 否 | 保留策略。 | `7d` |
| `receiver.ackMode` | enum | 否 | 确认策略。 | `AtLeastOnce` |
| `receiver.note` | string | 否 | 备注。 | `平台主接收端` |

## 3. `collector`

| 字段路径 | 类型 | 必填 | 说明 | 示例 |
| --- | --- | --- | --- | --- |
| `collector.collectorType` | enum | 是 | 采集模式。 | `IoTClawEdgeCollector` |
| `collector.runtime` | enum | 否 | 运行环境。 | `CSharp` |
| `collector.deploymentLocation` | enum | 是 | 部署位置。 | `EdgeGateway` |
| `collector.collectorId` | string | 否 | 采集器实例 ID。 | `edge-01` |
| `collector.bufferingEnabled` | bool | 否 | 是否缓存。 | `true` |
| `collector.offlineCache` | bool | 否 | 是否离线缓存。 | `true` |
| `collector.syncMode` | enum | 是 | 拉取 / 推送 / 订阅模式。 | `Poll` |
| `collector.owner` | string | 否 | 所属人或团队。 | `ops-team` |
| `collector.gatewayVendor` | string | 否 | 第三方网关厂商。 | `ThingsGateway` |
| `collector.gatewayProduct` | string | 否 | 第三方网关产品。 | `TG-Edge` |

## 4. `source`

| 字段路径 | 类型 | 必填 | 说明 | 示例 |
| --- | --- | --- | --- | --- |
| `source.sourceType` | enum | 是 | 数据源类型。 | `PLC` |
| `source.transport` | enum | 是 | 传输方式。 | `Serial` |
| `source.protocol` | enum | 是 | 工业或应用协议。 | `ModbusRTU` |
| `source.physicalLink` | enum | 是 | 物理链路。 | `RS485` |
| `source.address` | string | 否 | 地址、端口或节点。 | `COM3` |
| `source.deviceId` | string | 否 | 源设备 ID。 | `plc-01` |
| `source.pollingMode` | enum | 是 | 采集模式。 | `Poll` |
| `source.pollIntervalMs` | integer | 否 | 轮询间隔。 | `1000` |
| `source.subscribeTopic` | string | 否 | 订阅主题。 | `factory/a/telemetry` |
| `source.requestPath` | string | 否 | 请求路径。 | `/api/sensors` |
| `source.enabled` | bool | 否 | 是否启用。 | `true` |

### 4.1 `source.points[]`

| 字段路径 | 类型 | 必填 | 说明 | 示例 |
| --- | --- | --- | --- | --- |
| `source.points[].key` | string | 是 | 点位键名。 | `temperature` |
| `source.points[].name` | string | 否 | 显示名。 | `温度` |
| `source.points[].address` | string | 否 | 寄存器、地址或查询表达式。 | `40001` |
| `source.points[].registerType` | enum | 否 | 寄存器类型。 | `HoldingRegister` |
| `source.points[].dataType` | enum | 是 | 原始数据类型。 | `Float32` |
| `source.points[].length` | integer | 否 | 长度。 | `2` |
| `source.points[].byteOrder` | enum | 否 | 字节序。 | `Little` |
| `source.points[].wordOrder` | enum | 否 | 字序。 | `Reversed` |
| `source.points[].signedness` | enum | 否 | 有符号 / 无符号。 | `Unsigned` |
| `source.points[].scale` | number | 否 | 比例系数。 | `0.1` |
| `source.points[].offset` | number | 否 | 偏移量。 | `0` |
| `source.points[].bitMask` | string | 否 | 位掩码。 | `0xFF00` |
| `source.points[].bitShift` | integer | 否 | 位偏移。 | `8` |
| `source.points[].enumMap` | array | 否 | 枚举映射。 | `[{ "from": 0, "to": "off" }]` |
| `source.points[].expression` | string | 否 | 派生表达式。 | `value * 1.8 + 32` |
| `source.points[].unit` | string | 否 | 单位。 | `°C` |
| `source.points[].qualityRule` | string | 否 | 质量规则。 | `Clamp` |
| `source.points[].timestampRule` | string | 否 | 时间规则。 | `device-time` |
| `source.points[].enabled` | bool | 否 | 是否启用。 | `true` |
| `source.points[].note` | string | 否 | 备注。 | `主温度点位` |

## 5. `codec`

| 字段路径 | 类型 | 必填 | 说明 | 示例 |
| --- | --- | --- | --- | --- |
| `codec.byteOrder` | enum | 是 | 默认字节序。 | `Little` |
| `codec.wordOrder` | enum | 是 | 默认字序。 | `Reversed` |
| `codec.signedness` | enum | 否 | 默认符号位规则。 | `Unsigned` |
| `codec.defaultScale` | number | 否 | 默认缩放。 | `1` |
| `codec.defaultOffset` | number | 否 | 默认偏移。 | `0` |
| `codec.ruleMode` | enum | 否 | 规则应用方式。 | `PointOverrides` |
| `codec.rules[]` | array | 否 | 可复用转换规则。 | 见下文 |

### 5.1 `codec.rules[]`

| 字段路径 | 类型 | 必填 | 说明 | 示例 |
| --- | --- | --- | --- | --- |
| `codec.rules[].id` | string | 是 | 规则 ID。 | `swap-temp` |
| `codec.rules[].targetKey` | string | 是 | 目标点位键名。 | `temperature` |
| `codec.rules[].sourceAddress` | string | 否 | 源地址。 | `40001` |
| `codec.rules[].transformType` | enum | 是 | 转换类型。 | `SwapWords` |
| `codec.rules[].byteOrder` | enum | 否 | 字节序。 | `Little` |
| `codec.rules[].wordOrder` | enum | 否 | 字序。 | `Reversed` |
| `codec.rules[].signedness` | enum | 否 | 有符号 / 无符号。 | `Unsigned` |
| `codec.rules[].scale` | number | 否 | 缩放。 | `0.1` |
| `codec.rules[].offset` | number | 否 | 偏移。 | `0` |
| `codec.rules[].bitMask` | string | 否 | 掩码。 | `0xFF` |
| `codec.rules[].bitShift` | integer | 否 | 位移。 | `8` |
| `codec.rules[].enumMap` | array | 否 | 枚举映射。 | `[...]` |
| `codec.rules[].expression` | string | 否 | 表达式。 | `raw * 0.1` |
| `codec.rules[].unit` | string | 否 | 单位。 | `kWh` |
| `codec.rules[].enabled` | bool | 否 | 是否启用。 | `true` |

## 6. `uplink`

| 字段路径 | 类型 | 必填 | 说明 | 示例 |
| --- | --- | --- | --- | --- |
| `uplink.uplinkType` | enum | 是 | 上传方式。 | `MQTT` |
| `uplink.endpoint` | string | 是 | 上传端点。 | `mqtt://tb.example.com:1883` |
| `uplink.topicOrPath` | string | 否 | Topic 或路径。 | `v1/devices/me/telemetry` |
| `uplink.databaseName` | string | 否 | 数据库名。 | `iot_tsdb` |
| `uplink.tableName` | string | 否 | 表名。 | `telemetry` |
| `uplink.authMode` | enum | 是 | 上传认证。 | `AccessToken` |
| `uplink.credentialRef` | string | 否 | 凭据引用。 | `secret://uplink-token` |
| `uplink.payloadFormat` | enum | 是 | 上传格式。 | `JSON` |
| `uplink.qos` | integer | 否 | MQTT QoS。 | `1` |
| `uplink.batchSize` | integer | 否 | 批量大小。 | `100` |
| `uplink.timeoutMs` | integer | 否 | 超时。 | `5000` |
| `uplink.retryPolicy` | object | 否 | 重试策略。 | 见下文 |
| `uplink.backpressurePolicy` | enum | 否 | 背压策略。 | `Buffer` |
| `uplink.compression` | enum | 否 | 压缩方式。 | `Gzip` |
| `uplink.routeMode` | enum | 否 | 路由模式。 | `Single` |
| `uplink.routes[]` | array | 否 | 发送路由。 | 见下文 |

### 6.1 `uplink.retryPolicy`

| 字段路径 | 类型 | 必填 | 说明 | 示例 |
| --- | --- | --- | --- | --- |
| `uplink.retryPolicy.maxAttempts` | integer | 否 | 最大重试次数。 | `5` |
| `uplink.retryPolicy.backoffMs` | integer | 否 | 初始退避。 | `500` |
| `uplink.retryPolicy.maxBackoffMs` | integer | 否 | 最大退避。 | `30000` |
| `uplink.retryPolicy.jitterEnabled` | bool | 否 | 是否启用抖动。 | `true` |

### 6.2 `uplink.routes[]`

| 字段路径 | 类型 | 必填 | 说明 | 示例 |
| --- | --- | --- | --- | --- |
| `uplink.routes[].fromKey` | string | 是 | 来源点位键。 | `temperature` |
| `uplink.routes[].toPath` | string | 是 | 目标路径。 | `telemetry.temperature` |
| `uplink.routes[].transformRef` | string | 否 | 转换规则引用。 | `swap-temp` |
| `uplink.routes[].filter` | string | 否 | 路由过滤。 | `value > 0` |
| `uplink.routes[].enabled` | bool | 否 | 是否启用。 | `true` |

## 7. `governance`

| 字段路径 | 类型 | 必填 | 说明 | 示例 |
| --- | --- | --- | --- | --- |
| `governance.approvalMode` | enum | 否 | 审批模式。 | `PerRun` |
| `governance.riskLevel` | enum | 否 | 风险等级。 | `Medium` |
| `governance.auditEnabled` | bool | 否 | 是否审计。 | `true` |
| `governance.dataRetentionDays` | integer | 否 | 数据保留天数。 | `30` |
| `governance.notifyOnFailure` | bool | 否 | 失败是否通知。 | `true` |
| `governance.allowedTimeWindows` | string[] | 否 | 允许时间窗。 | `["08:00-18:00"]` |

## 8. 建议表单顺序

1. 接收终点
2. 采集器 / 网关
3. 来源协议与物理链路
4. 点位定义
5. 转换规则
6. 上传通道
7. 审批与审计

## 9. 机器可读契约

对应 JSON Schema: [schemas/iot-workspace-business-profile.schema.json](../schemas/iot-workspace-business-profile.schema.json)
