# RiverBox 插件发布指南

## 如何发布你的插件到插件仓库

### 1. 准备工作

#### 1.1 确保你的插件已编译
在 `RiverBox` 目录下运行：
```bash
dotnet build -c Release
```

#### 1.2 使用发布脚本
运行 `release-plugin.bat` 脚本自动打包插件：
```bash
release-plugin.bat
```

### 2. 创建GitHub Release

#### 2.1 推送代码到GitHub
```bash
git add .
git commit -m "准备发布RiverBox插件"
git push origin main
```

#### 2.2 创建Release
1. 访问你的GitHub仓库：https://github.com/Whswa-river/River
2. 点击 "Create a new release"
3. 选择标签（如 `v1.0.0`）
4. 上传你的ZIP文件作为Release资产
5. 保存Release

### 3. 更新插件仓库JSON

#### 3.1 更新版本号
在 `RiverBox.json` 中更新版本号：
```json
"AssemblyVersion": "1.0.0.0"  // 更新为实际版本号
```

#### 3.2 更新下载链接
确保下载链接指向正确的Release：
```json
"DownloadLinkInstall": "https://github.com/Whswa-river/River/releases/download/latest/latest.zip"
```

### 4. 分享你的插件仓库

用户可以通过以下方式使用你的插件：
1. 在Dalamud插件设置中添加你的JSON文件URL
2. 或者将你的插件提交到官方插件仓库

### 5. 插件仓库JSON字段说明

| 字段 | 说明 | 示例 |
|------|------|------|
| `Author` | 作者名称 | "River" |
| `Name` | 插件显示名称 | "RiverBox" |
| `InternalName` | 插件内部名称 | "RiverBox" |
| `Description` | 插件描述 | "RiverBox 自用插件" |
| `AssemblyVersion` | 版本号 | "1.0.0.0" |
| `RepoUrl` | GitHub仓库URL | "https://github.com/Whswa-river/River" |
| `DalamudApiLevel` | Dalamud API级别 | 15 |
| `Tags` | 插件标签 | ["Utilities"] |
| `IconUrl` | 插件图标URL | "https://raw.githubusercontent.com/Whswa-river/River/main/icon.png" |
| `DownloadLinkInstall` | 安装下载链接 | "https://github.com/Whswa-river/River/releases/download/latest/latest.zip" |
| `DownloadLinkTesting` | 测试版下载链接 | 同上 |
| `DownloadLinkUpdate` | 更新下载链接 | 同上 |

### 6. 提交到官方插件仓库（可选）

如果你想将插件提交到Dalamud官方插件仓库，需要：

1. **确保插件质量**：
   - 代码符合规范
   - 功能稳定可用
   - 有清晰的文档

2. **创建Pull Request**：
   - 访问 [Dalamud插件仓库](https://github.com/goatcorp/DalamudPlugins)
   - 添加你的插件到 `plugins.json` 文件
   - 创建Pull Request

3. **等待审核**：
   - 官方团队会审核你的插件
   - 可能需要修改以符合要求

### 7. 创建自己的插件仓库（推荐）

如果你不想提交到官方仓库，可以创建自己的插件仓库：

1. **创建仓库**：
   - 在GitHub上创建新仓库
   - 上传 `RiverBox.json` 和 `icon.png`

2. **用户安装方式**：
   - 在Dalamud插件设置中点击"从URL安装"
   - 输入你的JSON文件URL：
     ```
     https://raw.githubusercontent.com/Whswa-river/River/main/RiverBox.json
     ```

3. **维护更新**：
   - 每次发布新版本时，更新JSON文件中的版本号和下载链接
   - 保持插件图标和描述更新

### 8. 注意事项

- 确保Dalamud API级别与目标环境匹配
- 保持插件图标为PNG格式
- 定期更新版本号和下载链接
- 提供清晰的插件描述
- 遵守Dalamud插件开发规范
