# 组内进展说明 — ConnectOnion Desktop

写给还没怎么碰过客户端的同学。我和 A 主要做的是 Electron 桌面端这块；下面按「能干什么」一条条说。更细的技术文档在 `HANDOFF_CONTEXT.md`。

---

## 先怎么跑起来

第一次打开仓库，在 `frontend` 里 `npm install`，然后 `npm run desktop`。如果刚 pull 过代码、package.json 有变，一定要先 install，不然可能报找不到 vitest 之类。

聊天得连一个 agent。开发阶段我们是在本机另开终端跑 Python 示例 agent（比如 `simple-agent` 8020 端口），然后在 App 左侧 **Add Agent**，Direct URL 填 `http://127.0.0.1:8020`。以后给用户正式版时，一般是只发桌面客户端，agent 跑在云端，用户填 0x 或 App 里预置好就行——**不是让每个用户自己装 Python**。

---

## Agent 和 Session（都在左侧栏）

我们没做单独的 Agents 页面，`/agents` 会直接跳到聊天。所有事在**左边侧栏**完成。

**添加 agent：** 侧栏底部点 **+ Add Agent**，填显示名字，再填 Direct URL（本地 demo）或 0x 地址（连 relay）。可以只填 URL 不填 0x。

**测试连接：** 在添加表单里有个 **Test connection** 按钮。填好 URL 或地址后、点 Add 之前可以先测一下通不通。Direct URL 会去请求 `/health`；只填 0x 会走 SDK 查 relay 上在不在线。成功绿色勾，失败红色叉，带一句说明。你改了地址或 URL 输入框，上一次测试结果会自动清掉，避免看错。

**编辑 agent：** 鼠标移到某个 agent 那一行，会出现铅笔图标，点进去底部表单可以改名字、地址、URL，保存即可。不用删了重建（删了会换 id、历史关联也麻烦）。

**删除 agent：** 同一行 hover 有 X，会**弹确认框**，确认后删掉这个 agent 以及它下面所有 session。

**多 agent：** 可以加很多条，每条独立，聊天记录和 session 互不混。

**Session（会话）：** 每个 agent 下可以有多条对话。点 agent 名进入；左边小三角展开 session 列表。hover agent 行有 **+** 可以新建 session；点某条 session 切换过去。删 session 也有确认框。顶栏还有 **New chat** 按钮。

---

## 聊天窗口里有什么

选中 agent 和 session 后，右边就是聊天区。

**发消息：** 下面输入框，Enter 发送，Shift+Enter 换行。输入框右下角有灰色小字提示 `Enter to send · Shift + Enter for a new line`。没选 agent 时输入框是灰的，placeholder 会写 Add an agent first。

**收回复：** agent 的文字支持 Markdown（列表、链接、代码块等）。思考中会显示 Thinking 或 thinking 卡片；如果 agent 调了工具，会看到 tool 名称、参数、结果，可以点开看。

**长回复：** agent 回答特别长（大约七百字以上）可以 **Collapse / Expand** 折叠展开，默认是展开的。

**复制：** agent 回复可以 Copy，hover 名字旁边或下面有按钮，复制完会短暂显示 Copied。

**滚屏：** 你在底部看新消息时会自动滚到底；如果你往上翻历史，不会被强行拽回去。

**空对话：** 新 session 没消息时，输入框上面有几个小建议问题（比如 What can you do?），点一下会填进输入框。

**模式：** 输入框下面有个很小的 Mode 下拉（Safe / Plan / Accept edits / ULW），是 SDK 带的 approval 模式，一般 demo 用默认 Safe 就行。

**直连提示：** 如果是 Direct URL 连的，输入框上方会有一行小字显示 `Direct · http://...`。

**恢复历史：** 关掉 App 再开，之前的聊天记录会从本地读回来。切 session 时有时会先整页显示 **Loading conversation…**，等 SQLite 里那段的 SDK 状态加载完才进聊天。

---

## 断连、重连——两处提示要分开看

这块同学 demo 时容易注意到，单独说清楚。

**上面（聊天区顶栏，agent 名字那一行右边）：** 有个圆角状态条，里面是**彩色小圆点 + 英文状态**——Connected 绿点、Reconnecting… 琥珀色还会闪、Disconnected 灰点。后面还跟 agent 在干嘛：`idle` / `working` / `waiting`。

**只要没连上**（Disconnected 或 Reconnecting），状态条旁边会出现 **Reconnect** 按钮。点一下会调 SDK 重连。重连过程中按钮会变成 **Reconnecting**、图标转圈，并且**按钮是禁用的**，防止连点。

**下面（输入框正上方）：** 如果 SDK 报了连接类错误，会有一行**红底红字**的提示框，常见比如 **Reconnect timed out**（重连超时）。这是**输入区域上面、消息列表下面**那一块，和顶栏状态条是**分开的两处**——所以断连时你可能**同时**看到：上面灰点 Disconnected + Reconnect 按钮，下面红字 timed out。

**重连成功之后：** 顶栏变回绿点 Connected，**Reconnect 按钮会消失**（连上了就不显示了）。下面红字也会没——我们做了处理：一旦状态是 Connected，就不再显示那种**过期的连接错误**（SDK 自己有时不会清掉 error，所以客户端这边挡一下，避免上面已经绿了下面还挂着 timed out）。

**点 Reconnect 时** 也会先清掉你自己操作触发的发送错误，专心走重连。

**补充：** 如果你**不点 Reconnect**，直接再发一条消息，SDK 有时也会尝试自动连上；但顶栏 + Reconnect 按钮是给用户明确手动重连用的。

---

## 键盘快捷键（可以改绑）

**默认两个：**

- **Ctrl+B**（Mac 上也是记成 Ctrl+B）：显示/隐藏左侧栏。View 菜单里 Toggle sidebar 那一项右边会显示**当前绑定的键**。
- **Ctrl+N**：给**当前选中的 agent** 新开一条 session 并跳过去。

**改绑方法：** 侧栏 **Settings** → 往下滚到 **Keyboard shortcuts**。每一行左边是说明，右边是一个显示当前按键的小按钮。点一下按钮，变成 **Press keys…**，再按下你想要的组合键就保存；按 **Esc** 取消录制。如果你改过，会出现 **Reset** 可以恢复默认。

**录制时**不会误触发真正的快捷键（比如录 Ctrl+B 时侧栏不会跟着闪）。

**存哪：** 和主题、侧栏显隐一样，偏好会进 SQLite；改完下次打开还在。

**Help 菜单**里有一项 **Keyboard shortcuts**，点了会直接跳到 Settings 页，方便查。

---

## 深色模式和设置

**深色模式**大致做完了。View 菜单里可以切 Dark mode，Settings → Appearance 里也能切 Light/Dark，两处是同一个开关。聊天正文、Add Agent 表单、输入框、各种活动卡片在深色下都能看清字（我们专门修过一轮）。

**Settings 里还能调消息字号** S / M / L，只影响聊天正文大小。

**隐藏侧栏**除了 Ctrl+B，还可以 View → Toggle sidebar；藏掉之后聊天区全宽，下次打开会记住。

---

## 数据存在哪、为什么有时 Loading

以前全在浏览器 localStorage 里，现在 Electron 下**主要用 SQLite**，文件在：

`%APPDATA%\connectonion_desktop\storage\connectonion-desktop.sqlite3`

里面存 agent 列表、session 列表、聊天内容（SDK 那份 state）、主题侧栏字号快捷键等。启动 App 时会先从硬盘读到内存，所以刚打开侧栏可能短暂显示 **Loading agents…**，进聊天前也可能 **Loading…**，正常，一般很快。

如果 SQLite 或 IPC 出问题，会自动**降级**回 localStorage，App 不会直接崩。纯浏览器跑 `npm run dev`（不启 Electron）则仍用 localStorage。

老版本升上来时会**自动迁移一次** localStorage → SQLite，迁完会清掉旧的 localStorage 数据。

---

## 两种连 agent 的方式（常问）

**Direct URL：** 开发 demo 用这个。本机 `uvicorn` 起 agent，App 里填 `http://127.0.0.1:端口`。代码里有 directUrl 就走直连 WebSocket。

**0x + relay：** agent 部署在 ConnectOnion Cloud 或别人服务器上时用。App 里填 0x 地址，不用背，保存一次以后点名字就行。没填 Direct URL 就走 relay。

仓库 `agent/` 下有 simple、memory、email、browser 等示例，端口别撞就行，侧栏各加一条都能连，**不用改桌面端代码**。

---

## 标题栏和其它桌面小东西

顶部有 File / Edit / View / Help。**View 和 Help 能用**（深色、侧栏、快捷键跳转）；File / Edit 目前点了没反应，占位。

标题栏左侧还有 Back / Forward 小箭头（浏览器式历史），以及窗口最小化、最大化、关闭。开发模式下会自动开 DevTools，打包正式版不会。

输入框左边有个**回形针**图标，**还没有附件功能**，先别点着期待能传文件。

---

## 测试相关（同学后来加的）

仓库里加了 Vitest 单元测试和 Playwright E2E 脚手架。`frontend` 里 `npm run test`、`npm run e2e`。pull 之后记得 `npm install`。

---

## 还没做 / 别期待有

Windows 安装包（.exe）还没打。侧栏每个 agent 旁边**还没有**在线小绿点（连接状态只在聊天顶栏）。File/Edit 菜单、文件拖拽、系统托盘都还没做。README 里有些地方还写 localStorage-only，和现状不完全一致，以实际 App 为准。

---

## 开发时两个小提醒

改了 `electron/` 或 `preload.ts` 必须**关掉重开** `npm run desktop`，热更新不会带上主进程。

push 前在 `frontend` 跑一遍 `npm run lint` 和 `npm run build`，CI 比 IDE 严。

---

## 一句话

现在这个客户端已经能：侧栏管 agent 和 session、测连接、本地或 relay 聊天、SQLite 存记录、深色模式、顶栏+输入区双处断连提示和 Reconnect、复制和折叠长回复、可自定义快捷键。你们 pull 下来按上面跑 simple-agent + desktop 就能试；有问题看 `HANDOFF_CONTEXT.md` 或直接问我和 A。
