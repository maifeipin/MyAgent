# MyAgent - 项目设计与功能规格说明书 (Design & Specifications)

## 1. 系统概述 (System Overview)

MyAgent 是一个基于 .NET 8 开发的“白盒化” AI Agent 自动化系统。与传统的黑盒自动化工具不同，MyAgent 强调**执行过程的可视化、可审计性以及系统的自我进化能力**。它集成了无头浏览器控制、交互式终端运维以及动态技能引擎，旨在建立一个用户可控、AI 自律的高效生产力平台。

---

## 2. 系统架构 (Architecture)

MyAgent 采用三层解耦架构，确保了各个组件的独立性与可扩展性：

### 2.1 核心层 (MyAgent.Core)
- **技能引擎 (Skill Engine)**：驱动自动化流程的核心调度器，负责解析 YAML 格式的分步工作流。
- **动作路由 (Action Router)**：利用反射机制动态加载和调用各种原子操作（如 `browser.open`, `terminal.exec`）。
- **数据持久化**：采用 SQLite 存储所有的执行审计日志 (`StepLogs`) 和性能统计 (`Analytics`)。

### 2.2 技能集 (MyAgent.Skills)
- **原子动作库**：包含了浏览器控制逻辑 (WebView2)、SSH/终端通信 (SSH.NET)、文件系统操作等插件化功能。
- **YAML 模板库**：存储预定义的自动化任务脚本，支持 AI 在运行时动态生成和更新这些脚本。

### 2.3 UI 表现层 (MyAgent.UI)
- **实时监控窗 (WebView2)**：同步展示 AI 在浏览器中的每一个点击和 DOM 交互过程。
- **全双工终端 (xterm.js)**：为 AI 和用户提供一个共享的、实时的 Linux/PowerShell 执行窗口。
- **配置大管家**：支持多模型预设切换，并实现所有配置（含 API Keys）的本地加密持久化。

---

## 3. 核心功能特性 (Key Features)

### 3.1 动态自我进化机制 (Self-Evolution)
- **代码自读**：AI 具备精读自身源码及配置文件的能力。
- **参数覆写**：通过 `agent.update_config` 动作，AI 可以根据任务需求动态修改自身的网络代理、大模型节点等底层参数。
- **技能生成**：AI 可以自主编写并保存新的 YAML 技能模板，实现能力的无限扩展。

### 3.2 可视化浏览器自动化 (Observable Browser Control)
- 基于 Microsoft Edge WebView2 深度集成。
- AI 操作不再是“黑盒”，用户可以实时盯盘，观察 AI 的页面导航、元素选择及数据抓取路径。

### 3.3 交互式 O&M 终端 (Interactive Terminal)
- 集成 xterm.js，支持本地 Shell 以及远程 SSH 连接。
- AI 能够理解控制台输出，并根据反馈调整后续的命令行指令，实现高度智能化的运维自动化。

---

## 4. 技术规格 (Technical Specifications)

- **开发框架**：.NET 8 (WPF + C#)
- **AI 协议**：OpenAI 兼容接口协议 (支持 Qwen, Gemini, DeepSeek 等)
- **前端组件**：WebView2 (浏览器), xterm.js (终端)
- **数据存储**：SQLite (Dapper ORM)
- **配置格式**：JSON (系统配置), YAML (技能描述)

---

## 5. 安全与隐私 (Security & Privacy)

- **凭证脱敏**：所有 API Key 仅在本地加密保存，永不回传。
- **执行审计**：SQLite 数据库记录每一步的 `RawInput` 和 `RawOutput`，确保所有 AI 行为可溯源。
- **沙箱环境**：浏览器和终端进程可受限运行，防止误触高危指令。

---

> 项目版本：v1.0.0-Stable
> 维护者：maifeipin
