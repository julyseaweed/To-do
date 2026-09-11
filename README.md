# To-do

A minimal desktop list for Windows, with frosted glass, colorful topics, and direct editing. Everything stays in local files; no account is required.

一款简洁的 Windows 桌面清单，采用毛玻璃背景、彩色主题列和原位编辑。所有内容保存在本地，无需账户。

## Features / 功能

- Topics run horizontally, with independently scrolling lists and a separate Watching list for names and links.
  主题横向排列，各列独立滚动；左侧 Watching list 单独存放名称和链接。
- Click to type, press Enter to add a row, and drag cards to reorder. Colors follow their position in the gradient.
  点击即可输入，回车添加下一行，拖动色块调整顺序；渐变按位置自然衔接。
- Complete an item with its circle. Delete empty cards or topics with Backspace / Delete, and restore changes with Undo or Ctrl+Z.
  点击圆圈完成事项；清空后再按删除键可删除色块或主题，支持 Undo 和 Ctrl+Z 撤销。
- Move the panel freely, scale it from a corner, or collapse it to a small floating circle.
  自由移动面板，从四角整体缩放，也可以收起为小圆形浮标。
- Adjust transparency, choose Always on top, and optionally enable Open at login. The interface is in English.
  可调整透明度、选择置顶和开机启动。应用界面为英文。

## Download and run / 下载使用

Requires **Windows 11 x64**. No Node.js, browser, or additional .NET installation is needed on a standard Windows 11 system.

需要 **Windows 11 x64**。标准 Windows 11 系统无需额外安装 Node.js、浏览器或 .NET。

1. Download **To-do-windows-x64.zip** from [Releases](https://github.com/julyseaweed/To-do/releases/latest).
   在 Releases 页面下载 **To-do-windows-x64.zip**。
2. Extract the whole ZIP to a dedicated folder you can write to, such as a To-do folder under Documents.
   将压缩包完整解压到独立、可写的文件夹，例如文档中的 To-do 文件夹。
3. Double-click **To-do.exe**. A new installation starts with an empty Watching list; add your own topics and items.
   双击 **To-do.exe**。首次打开只有空白的 Watching list，主题和事项由你自行添加。

To create a desktop shortcut, open PowerShell in the extracted folder and run:

如需创建桌面快捷方式，在解压目录打开 PowerShell，运行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Install.ps1
```

This links to the app in its current folder. Keep that folder in place, or rerun the installer after moving it. Login startup is off by default; use **… → Open at login** in the app, or add `-OpenAtLogin` to the command above. Installing again preserves an existing enabled startup setting.

快捷方式指向当前目录中的应用，请保留该目录；移动目录后重新运行安装脚本。默认不启用开机启动，可在应用的 **… → Open at login** 中开启，或在上面的命令末尾加上 `-OpenAtLogin`。再次安装会保留已启用的开机启动设置。

## Everyday use / 日常操作

| Action / 操作 | How / 方式 |
| --- | --- |
| Add a topic / 添加主题 | **New topic**; type directly into its heading / 直接在标题处输入 |
| Add an item / 添加事项 | Column **+**, or **Enter** while editing / 点击列内加号，或编辑时回车 |
| Delete a card or topic / 删除色块或主题 | Clear its text, then press **Backspace / Delete** again / 清空文字后再按一次删除键 |
| Undo / 撤销 | **Undo** or **Ctrl+Z** |
| Open a saved link / 打开链接 | Arrow beside the item / 条目右侧的箭头 |
| Collapse / 收起 | **−**; drag the circle, then click to expand there / 可拖动圆形浮标，点击后就地展开 |
| Hide / 隐藏 | **×**; reopen from the desktop shortcut or tray / 点击桌面快捷方式或托盘图标重新打开 |
| Exit fully / 完全退出 | Tray icon → **Quit** / 托盘图标菜单中的 Quit |

Blank rows and unnamed topics are kept. Deleting a topic also removes its contents; Undo restores the whole column. The Watching list heading can be renamed, and its divider can be dragged to adjust the column width.

空白行和未命名主题都会保留。删除主题时会同时删除该列内容，撤销可恢复整列。Watching list 的标题可以修改，拖动分隔线可调整它的宽度。

## Local files and updates / 本地文件与更新

Data is created beside the executable on first use:

首次使用时，在应用旁创建数据目录：

```text
To-do.exe
UserData/
  notes/当前.md          # Lists / 清单
  settings.json          # Preferences / 设置
  backups/               # Recovery copy / 恢复副本
```

Lists autosave as Markdown. The repository and release ZIP contain no personal lists, settings, screenshots, or saved desktop positions. The app has no account system, cloud sync, or telemetry; opening a saved link uses your browser.

清单自动保存为 Markdown。仓库与发行压缩包不包含个人清单、设置、截图或保存的桌面位置。应用没有账户系统、云同步或遥测；打开已保存的链接会使用浏览器。

To update, choose **Quit** from the tray, then replace the application files with the new release. Keep **UserData** intact. Back up that folder before moving to another computer; an explicitly configured external notes folder must be copied separately.

更新前从托盘菜单选择 **Quit**，再用新版本替换应用文件，保留 **UserData**。换电脑前请备份整个数据目录；如果手动指定过外部笔记目录，还需单独复制该目录。

## Build from source / 从源码构建

Uses C# and WPF with the .NET Framework compiler included in Windows. Clone or download this repository, then run in PowerShell:

使用 C#、WPF 和 Windows 自带的 .NET Framework 编译器。克隆或下载仓库后，在 PowerShell 中运行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
.\artifacts\To-do.Tests.exe
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Install.ps1
```

The app is built to `artifacts/To-do.exe`. To create a clean distributable ZIP without local data, run:

应用生成在 `artifacts/To-do.exe`。运行以下命令可创建不含本地数据的发行压缩包：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Package.ps1
```

Additional interaction and rendering checks are in `tests/`.

其他交互与渲染检查位于 `tests/`。

You can also open this repository in Codex and use this prompt:

也可以把仓库交给 Codex，并复制这段提示：

> Build and install To-do from this repository on my Windows computer. Use a dedicated writable folder, create a desktop shortcut, and keep login startup optional. Preserve existing lists and do not upload any personal data.
>
> 请从当前仓库在我的 Windows 电脑上构建并安装 To-do，使用独立可写目录，创建桌面快捷方式，开机启动由我选择。保留已有清单，不上传任何个人数据。
