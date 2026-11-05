# Fowan（伏案）Logo Development Assets

本素材包基于已选定的 Fowan Logo 方向整理，面向 Windows / macOS / iOS / Android / Web 开发使用。

## 品牌概念

- 中文名：伏案
- 英文名：Fowan
- 展开句：Fowan Orchestrates Workflows with AI, Natively.
- 图形概念：由 **F + A** 组成的极简字母组合。
  - **F**：像一个伏案工作的人，短横象征手臂/手。
  - **A**：像桌面与电脑，横线延展成桌面，上半部分形成屏幕。
  - 整体表达“伏案工作 + 原生桌面智能 + AI 工作流协同”。

## 目录说明

```text
source/                         选定版本的品牌展示板 PNG
svg/                            可编辑 SVG 源文件
png/mark/                       透明背景 Logo 图形导出
png/logo/                       横版 Logo PNG 导出
png/app-icon/                   通用 App Icon PNG 尺寸
windows/                        Windows ico 与常用 PNG
macos/AppIcon.iconset/          macOS iconset，可用 iconutil 转 icns
ios/AppIcon.appiconset/         Xcode 可用 iOS AppIcon.appiconset
android/res/                    Android launcher icon 资源
tokens/                         颜色与品牌 token
web/                            favicon / apple-touch-icon
```

## 推荐使用

### Windows

使用 `windows/fowan-app-icon-256.png` 或 `windows/fowan-app-icon-512.png` 作为安装器/商店图标来源。Windows 应用使用 Toolbox 的多尺寸品牌 ICO，避免低分辨率图标在高 DPI 下被放大。

### 工具箱专属图标

工具箱的卡片、列表和详情页共用同一套专属图标渲染规范：图形的可见主体应与 AI 对话图标保持相同的视觉占比，标准 256px 画布预留约 16px 的透明安全边距。新图标应按这一画布规范导出；如历史图标的透明边距不同，只能在 `ToolboxControlFactory` 的专属图标注册表中设置画布缩放，以保证三个入口始终使用相同的视觉尺寸。

### macOS

已生成 `macos/AppIcon.iconset/`。在 macOS 上可执行：

```bash
iconutil -c icns AppIcon.iconset
```

生成 `AppIcon.icns` 后放入 Xcode / App Bundle。

### iOS

直接将以下目录拖入 Xcode Assets：

```text
ios/AppIcon.appiconset
```

### Android

将以下目录合并进 Android 项目的 `app/src/main/res/`：

```text
android/res/
```

### Web

使用：

```text
web/favicon.ico
web/favicon.svg
web/apple-touch-icon.png
```

## 颜色

| Token | Hex | 用途 |
|---|---|---|
| Fowan Navy | `#0A1A3D` | 主 Logo、深色背景 |
| Fowan Blue | `#1E5BFF` | 电脑屏幕/AI 高亮 |
| Fowan Gray | `#8A95A8` | 辅助文字、分割线 |
| White | `#FFFFFF` | 反白 Logo、背景 |

## 注意事项

1. 本包中的 SVG 是基于已选概念重建的开发可用矢量稿，适合开发接入、原型和早期发布使用。
2. `Fowan` 与 `伏案` 在 SVG 中保留为可编辑文本，使用系统字体回退；正式商用前建议在 Figma / Illustrator 中确定最终字形并转曲。
3. 包内不包含任何字体文件。
4. 商标注册、版权归属、应用商店审核图标规范仍需在正式发布前单独确认。

## Android 额外说明

`android/res/drawable/fowan_mark_foreground.xml` 是可编译的 VectorDrawable；`mipmap-*` 中也包含传统 PNG launcher icon。

## 深色背景 Logo

需要在深色背景上使用横版 Logo 时，可使用：

```text
svg/fowan-logo-horizontal-white.svg
png/logo/white/
```
