# MyAgent: 透明、白盒化的 AI Agent 本地协作系统

MyAgent 是一款基于 C# (WPF) 构建的现代化原生桌面客户端，旨在打破传统 AI 自动化工具的“黑盒”困境。通过 **YAML 白盒编排**、**实况浏览器 (WebView2)** 和 **交互式终端 (xterm.js)**，MyAgent 让大模型的每一次决策和动作都清晰地展现在您的眼皮底下。

## 🌟 核心特性

- **灵魂进化**：具备精读代码与自我重构能力。AI 可以分析自身配置，并利用 `agent.update_config` 实现自我进化。
- **实况浏览器 (WebView2)**：物理嵌入 Chromium 内核，实时展示 AI 的网页导航、DOM 交互与搜索意图，拒绝后台“盲盒”操作。
- **可视化运维专家**：内置 **xterm.js** 终端子系统，支持 AI 通过 SSH 与服务器进行全双工交互，自动处理回显并修正指令。
- **YAML 技能剧本**：任务基于白盒编排，支持动态组装与 AI 意图寻址推理。
- **极致网络穿透**：内核级代理注入，支持全链路 Socks5/HTTP 代理，无缝连接全球主流大模型 API。
- **多端通知**：集成 Webhook 功能，支持将执行报告一键推送到钉钉、飞书等企业办公软件。

## 🚀 快速开始

### 环境依赖
- Windows 10/11
- .NET 8.0 SDK
- WebView2 Runtime

### 运行
1. 克隆项目：`git clone https://github.com/your-username/MyAgent.git`
2. 使用 Visual Studio 2022 打开 `MyAgent.slnx` 或 `src/MyAgent.sln`。
3. 配置 `user_config.json` 中的 AI API 密钥（可参考 `backup/user_config.json`，如果已备份）。
4. 编译并启动 `MyAgent.UI`。

## 🛠️ 技术架构

- **UI 层**：WPF (MVVM 模式)，集成 WebView2 和 xterm.js (via HTML5 bridge)。
- **核心引擎**：基于 .NET 8 的自研白盒编排引擎，支持 YAML 技能解析。
- **技能系统**：高度可扩展的 ActionTool 插件机制。

## 📝 开源协议
本项目采用 **MIT License**。详见 [LICENSE](LICENSE)。

---
> “真正的 AI 代理，应当是你手中的尖刀，而不是脱缰的野马。”
