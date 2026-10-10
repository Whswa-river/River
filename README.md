# River

SFY 的 FFXIV Dalamud 插件仓库。

## 插件列表

| 插件 | 说明 | 最新版本 |
|------|------|---------|
| **AutomaticHunting** | 自动狩猎跟车 — 监听车头 Flag，自动导航到狩猎目标 | 2.2.27.0 |
| **AllPotPredictor** | 全大区魔法罐预测 — 汇总四大区众包网的魔法罐预测时间 | 1.4.2.0 |
| **RiverBox** | 自用工具箱 — 自动收艇、随心而行、场地标点导航、插件管理 | 1.0.11.1 |

> 版本号可能滞后，以 [Releases](https://github.com/Whswa-river/River/releases) 为准。

## 安装

### 方式一：合集仓库（推荐）

在 Dalamud → 设置 → 插件仓库 中添加一个地址，即可安装全部插件：

```
https://raw.githubusercontent.com/Whswa-river/PluginRepo/main/plugins.json
```

### 方式二：单插件仓库

只装某一个插件时，添加对应清单地址：

```
https://raw.githubusercontent.com/Whswa-river/River/main/AutomaticHunting.json
https://raw.githubusercontent.com/Whswa-river/River/main/AllPotPredictor.json
https://raw.githubusercontent.com/Whswa-river/River/main/RiverBox.json
```

## 环境要求

- Dalamud API Level 15（XIVLauncherCN 国服启动器）
- 部分功能依赖以下插件：
  - **vnavmesh** — 自动寻路导航（狩猎跟车、场地标点传送）
  - **DailyRoutines** — 传送、收艇等增强指令
  - **Lifestream** — 特殊场景传送

## 仓库结构

```
River/
├── AutomaticHunting.json      # 单插件清单（版本/下载链接）
├── AllPotPredictor.json
├── RiverBox.json
├── AutomaticHunting/          # 插件图标
│   └── icon.png
├── RiverBox/                  # RiverBox 源码
│   └── icon.png
└── release.py                 # RiverBox 发布脚本
```

插件实体文件统一发布在 [Releases](https://github.com/Whswa-river/River/releases) 页，根目录 JSON 只是清单索引。

## 反馈

问题与建议请提 [Issue](https://github.com/Whswa-river/River/issues)。
