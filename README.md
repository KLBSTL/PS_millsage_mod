# 米尔赛奇：旧物与余音

《Probably Stolen》Windows Steam 正式版的剧情与经营 Mod。你会在自己的旧物店接待米尔赛奇的成员，经由柜台来访、器材整备、电话预约和演出推进一条完整故事线。剧情采用「独读」「独自离场」路线，共 31 场。

## 下载与安装

1. 在 [GitHub Releases](https://github.com/KLBSTL/PS_millsage_mod/releases) 下载 **PS_Millsage_Mod_v0.37.4_Windows.zip**。
2. 退出游戏。在 Steam 库右键游戏，选择 **管理 → 浏览本地文件**，打开含有 `Probably Stolen.exe` 的正式版安装目录。
3. 把 ZIP 内的 `Mods`、`ModDevelopment`、`ModWorkbench` 三个文件夹解压到这里。Windows 提示时合并同名目录。安装后应能看到 `Mods/Millsage/manifest.xml`。
4. 启动游戏，在主菜单的 **MOD** 中勾选 **米尔赛奇：旧物与余音**。首次启用时，游戏可能要求确认清单里的文件和运行权限。按游戏提示重启。
5. 新建一个专用于本 Mod 的存档，正常开店，留意柜台来访、物品便条和电话。Mod 启用期间会优先使用简体中文界面与物品文本，游戏原有的语言选项会保留。

请使用 Release 的 Windows 安装包。GitHub 自动生成的 **Source code** 压缩包用于维护源码。运行使用游戏自带的 MOD 加载器，预编译 DLL 和运行依赖已经随包提供。兼容性以 Steam Windows 正式版 **BuildID 26337366** 为基准。

## 游玩内容

- **剧情**通过开店来访、阅读便条、电话和赴约展开。部分事件需要过日或先完成上一件事。
- **手持检测**先选基础或专业检测仪，再对要检查的器材使用。报告用原版式窗口显示，器材留在原处。
- **整备**把器材和配件放到电声整备台，工具装入原版工具箱后放进机器的工具箱槽。把鼠标悬停在整备台右侧的纸条上可查看说明。待装仓可用扩仓套件升级。
- **委托**通过原版柜台接件、报价或议价。检测或加工后按便条提示联系客户，在约定日交回同一件器材。
- **经营**包含陈列、批发供货、芯片打印、拆解、精密鉴定和成本账册，按游玩进度开放。

公开包的场馆使用背景和原生对白界面。实时 Live2D、第三方角色立绘和测试配音属于本地开发环境。本版已核验两期普通租金及一笔收费检测交回。普通资金下的 1800 整备台采购、多轮收费委托与完整人工鼠标复测仍待验收。

## 更新、停用与存档

- **更新**前退出游戏，备份 Mod 存档，再把新版安装包解压到同一安装目录并覆盖同名文件。
- **回到原版**时，在游戏内取消勾选本 Mod 并重启，然后读取原版存档。含有 Mod 物品、工单或剧情进度的存档请在 Mod 启用时读取。
- **备份**可复制 Windows 上 `%USERPROFILE%\AppData\LocalLow\Questing Goose Studio\Probably Stolen` 中的存档文件。安装包自身没有存档。
- **列表里找不到 Mod**时，确认游戏目录下直接存在 `Mods/Millsage/manifest.xml`，并确认 `ModDevelopment`、`ModWorkbench` 和 `Mods` 是同级目录。启用后仍看不到内容时，确认完成重启且使用正式版。

## 源码与构建

仓库跟踪核心 C#、运行所需的剧情和自制贴图，以及构建脚本。测试驱动、隔离宿主和实机存档保存在开发环境。公开源码保留一层用于编译既有分支的兼容类型，测试会话始终为空。安装 ZIP 包含运行文件与说明。

用 PowerShell 7、.NET SDK 9.0.301 和 Mono.Cecil 0.11.6，从本机安装的正式版读取编译引用。将 Mono.Cecil NuGet 包解压后，指向其中 `lib/netstandard2.0/Mono.Cecil.dll`。

```powershell
./Build.ps1 -GameRoot 'D:/SteamLibrary/steamapps/common/Probably Stolen' -CecilPath 'D:/tools/cecil/lib/netstandard2.0/Mono.Cecil.dll'
```

安装包及 SHA-256 校验文件生成在 `dist/`。编译过程只读取已安装的游戏，游戏程序集及编译引用保留在本地。随安装包提供的 .NET 组件附带许可证及第三方声明。游戏和角色名称归其各自权利方所有。本 Mod 是独立的同人项目。
