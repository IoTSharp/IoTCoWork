# IoTCoWork 物联网业务方案蓝图 v0.1

> 目标：把一个 workspace 的业务上下文固定下来，确保它始终回答五个问题：数据给谁、谁采、从哪采、怎么转、怎么传。

## 1. 结论

一个 IoTCoWork workspace 必须内置一份业务方案。

这份方案不是描述性文字，而是可校验、可生成、可下发的结构，至少包含：

1. 数据上传给谁
2. 谁来采集
3. 数据从哪里来、怎么采
4. 数据如何转换
5. 通过什么通道上传

如果这五项不完整，workspace 就不算可执行。

## 2. 五个必填维度

### 2.1 数据接收终点

回答“数据上传给谁”。

可选终点类型：

- `IoTSharp`
- `ThingsBoard`
- `AlibabaCloudIoTPlatform`
- `TimeSeriesDatabase`
- `CustomApplicationApi`
- `CustomBrokerOrQueue`

建议字段：

- `targetType`
- `vendor`
- `instanceId`
- `tenantOrProject`
- `endpoint`
- `authMode`
- `protocol`
- `payloadFormat`
- `topicOrPath`
- `retentionPolicy`
- `ackMode`

### 2.2 数据采集器 / 网关

回答“谁来采集”。

可选模式：

- `DirectDeviceCode`：设备自己写代码采集
- `IoTCoWorkEdgeCollector`：使用我们的边缘采集
- `ThirdPartyGateway`：使用第三方网关
- `Hybrid`：部分点位直采，部分点位由网关转发

第三方网关可以作为可插拔采集层，例如 ThingsBoard IoT Gateway 或 ThingsGateway。

建议字段：

- `collectorType`
- `runtime`
- `deploymentLocation`
- `collectorId`
- `bufferingEnabled`
- `offlineCache`
- `syncMode`
- `owner`

### 2.3 数据来源与协议

回答“数据从哪里来、如何采集”。

常见来源：

- 传感器
- PLC
- 仪表
- 设备控制器
- 本地数据库
- 外部 API
- 文件流

常见采集链路：

- `HTTP` / `HTTPS`
- `MQTT`
- `Modbus TCP`
- `Modbus RTU`
- `OPC UA`
- `BACnet IP`
- `BACnet MS/TP`
- `Serial`
- `I2C`
- `CAN`
- 自定义二进制协议

建议字段：

- `sourceType`
- `transport`
- `protocol`
- `physicalLink`
- `address`
- `pollingMode`
- `pollInterval`
- `subscribeTopic`
- `requestPath`
- `deviceId`
- `registerMap`

### 2.4 数据转换与编码

回答“数据如何转换”。

这部分必须支持规则化描述，不能只靠口头约定。

常见转换：

- 高低位翻转
- 字节序转换
- Word 顺序调整
- 有符号 / 无符号转换
- 整数 / 浮点转换
- BCD 转换
- 位域提取
- 枚举映射
- 比例系数缩放
- 偏移量修正
- 温度 / 压力 / 流量单位换算
- 时间戳归一
- 空值 / 异常值修正
- 派生指标计算

建议字段：

- `byteOrder`
- `wordOrder`
- `endianness`
- `signedness`
- `scale`
- `offset`
- `bitMask`
- `enumMap`
- `formula`
- `unit`
- `qualityRule`
- `timestampRule`

### 2.5 数据上传通道

回答“通过什么上传”。

常见通道：

- `MQTT`
- `HTTP`
- `HTTPS`
- `DatabaseWrite`
- `CoAP`
- `AMQP`
- `Kafka`
- `FileBatch`

建议字段：

- `uplinkType`
- `endpoint`
- `topicOrPath`
- `authMode`
- `qos`
- `batchSize`
- `retryPolicy`
- `timeout`
- `backpressurePolicy`
- `compression`

## 3. 推荐工作区结构

建议每个 workspace 保存一份统一业务模型：

```json
{
  "schemaVersion": "0.1",
  "workspaceId": "ws-demo",
  "workspaceName": "一号产线能耗采集",
  "displayName": "A-Line Energy",
  "receiver": {
    "targetType": "ThingsBoard",
    "endpoint": "https://tb.example.com",
    "authMode": "AccessToken",
    "protocol": "MQTT",
    "payloadFormat": "JSON",
    "topicOrPath": "v1/devices/me/telemetry"
  },
  "collector": {
    "collectorType": "IoTCoWorkEdgeCollector",
    "deploymentLocation": "EdgeGateway",
    "syncMode": "Poll",
    "bufferingEnabled": true,
    "offlineCache": true
  },
  "source": {
    "sourceType": "PLC",
    "transport": "Serial",
    "protocol": "ModbusRTU",
    "physicalLink": "RS485",
    "address": "COM3",
    "pollingMode": "Poll",
    "pollIntervalMs": 1000,
    "points": [
      {
        "key": "temperature",
        "name": "温度",
        "address": "40001",
        "registerType": "HoldingRegister",
        "dataType": "Float32",
        "length": 2,
        "byteOrder": "Little",
        "wordOrder": "Reversed",
        "signedness": "Unsigned",
        "scale": 0.1,
        "offset": 0,
        "unit": "°C",
        "enabled": true
      }
    ]
  },
  "codec": {
    "byteOrder": "Little",
    "wordOrder": "Reversed",
    "signedness": "Unsigned",
    "defaultScale": 0.1,
    "defaultOffset": 0,
    "ruleMode": "PointOverrides"
  },
  "uplink": {
    "uplinkType": "MQTT",
    "endpoint": "mqtt://tb.example.com:1883",
    "topicOrPath": "v1/devices/me/telemetry",
    "authMode": "AccessToken",
    "payloadFormat": "JSON",
    "qos": 1,
    "batchSize": 100,
    "timeoutMs": 5000,
    "retryPolicy": {
      "maxAttempts": 5,
      "backoffMs": 500,
      "maxBackoffMs": 30000,
      "jitterEnabled": true
    },
    "routeMode": "Single"
  },
  "governance": {
    "approvalMode": "PerRun",
    "riskLevel": "Medium",
    "auditEnabled": true,
    "dataRetentionDays": 30,
    "notifyOnFailure": true
  }
}
```

这只是概念模型，后续可以映射成表单、数据库、插件契约或 YAML 模板。

## 4. 典型业务方案

### 4.1 设备直连平台

- 设备自己采集
- 直接发到 IoTSharp、ThingsBoard 或阿里云 IoT 平台
- 适合设备能力足够、协议简单的场景

### 4.2 边缘采集后上云

- 设备先接入 IoTCoWork Edge 或第三方网关
- 网关负责采集、转换、缓存、重试
- 再把数据发到平台或 TSDB

### 4.3 第三方网关接管

- 使用 ThingsBoard IoT Gateway、ThingsGateway 等现成网关
- IoTCoWork 主要负责配置、调试、观察和统一建模

### 4.4 直接入时序数据库

- 采集后直接写入 TSDB
- 适合分析、回放、报表优先的场景
- 平台层再从 TSDB 消费

### 4.5 直接入业务应用

- 通过 HTTP API 或其他业务接口上报
- 适合某个应用先跑通的场景
- 后续再统一接入平台

## 5. 建议的产品规则

1. 一个 workspace 至少绑定一个接收终点。
2. 一个 project 至少绑定一个采集器方案。
3. 每条数据链路都必须有明确协议和上传方式。
4. Modbus / OPC UA / BACnet 这类工业协议必须显式定义字节序和映射规则。
5. MQTT / HTTP 这类上传通道必须明确认证、重试和批量策略。

## 6. 对 IoTCoWork 的实现含义

这份业务方案将直接驱动：

- workspace 创建向导
- 项目模板
- 协议调试器
- 转换规则编辑器
- 上传配置页
- 边缘发布页
- AI 任务建议与自动生成

## 7. 参考资料

- ThingsBoard 主仓库: https://github.com/thingsboard/thingsboard
- ThingsBoard HTTP telemetry: https://thingsboard.io/docs/reference/http-api/
- ThingsBoard MQTT telemetry: https://thingsboard.io/docs/paas/reference/mqtt-api/
- ThingsBoard telemetry guide: https://thingsboard.io/docs/user-guide/telemetry/
- ThingsBoard Gateway: https://github.com/thingsboard/thingsboard-gateway
- ThingsBoard Gateway docs: https://thingsboard.io/docs/iot-gateway/
- Modbus connector: https://thingsboard.io/docs/iot-gateway/config/modbus/
- OPC-UA connector: https://thingsboard.io/docs/iot-gateway/config/opc-ua/
- BACnet connector: https://thingsboard.io/docs/iot-gateway/config/bacnet/
- MQTT connector: https://thingsboard.io/docs/iot-gateway/config/mqtt/
- REST connector: https://thingsboard.io/docs/iot-gateway/config/rest/
- Alibaba Cloud IoT HTTPS report: https://www.alibabacloud.com/help/doc-detail/146160.html
- Alibaba Cloud IoT device connection overview: https://www.alibabacloud.com/help/doc-detail/2248464.html
- Alibaba Cloud IoT MQTT protocol: https://www.alibabacloud.com/help/en/iot/user-guide/mqtt-protocol
- Alibaba Cloud IoT MQTT gateway: https://www.alibabacloud.com/help/en/iot/user-guide/mqtt-gateways
- ThingsGateway: https://github.com/ThingsGateway/ThingsGateway

## 8. 可执行契约

- 字段表: [iot-workspace-business-profile-fields.md](iot-workspace-business-profile-fields.md)
- JSON Schema: [../schemas/iot-workspace-business-profile.schema.json](../schemas/iot-workspace-business-profile.schema.json)
