## 先怎么跑起来

第一次打开仓库，在 `frontend` 里 `npm install`，然后 `npm run desktop`。如果刚 pull 过代码、package.json 有变，一定要先 install，不然可能报找不到 vitest 之类。

聊天得连一个 agent。开发阶段我们是在本机另开终端跑 Python 示例 agent（比如 `simple-agent` 8020 端口），然后在 App 左侧 **Add Agent**，Direct URL 填 `http://127.0.0.1:8020`。以后给用户正式版时，一般是只发桌面客户端，agent 跑在云端，用户填 0x 或 App 里预置好就行——**不是让每个用户自己装 Python**。

---

## Agent 和 Session（都在左侧栏）

我们没做单独的 Agents 页面，`/agents` 会直接跳到聊天。所有事在**左边侧栏**完成。

**添加 agent：** 侧栏底部点 **+ Add Agent**，填显示名字，再填 Direct URL（本地 demo）或 0x 地址（连 relay）。可以只填 URL 不填 0x。

**测试连接：** 在添加表单里有个 **Test connection** 按钮。填好 URL 或地址后、点 Add 之前可以先测一下通不通。Direct URL 会去请求 `/health`；只填 0x 会走 SDK 查 relay 上在不在线。成功绿色勾，失败红色叉，带一句说明。

**编辑 agent：** 鼠标移到某个 agent 那一行，会出现铅笔图标，点进去底部表单可以改名字、地址、URL，保存即可。不用删了重建（删了会换 id、历史关联也麻烦）。

**删除 agent：** 同一行 hover 有 X，会**弹确认框**，确认后删掉这个 agent 以及它下面所有 session。

**多 agent：** 可以加很多条，每条独立，聊天记录和 session 互不混。

**Session（会话）：** 每个 agent 下可以有多条对话。点 agent 名进入；左边小三角展开 session 列表。hover agent 行有 **+** 可以新建 session；点某条 session 切换过去。删 session 也有确认框。顶栏还有 **New chat**，快捷键默认 **Ctrl+N**（可在 Settings 里改）。

---

## 聊天窗口里有什么

选中 agent 和 session 后，右边就是聊天区。

**发消息：** 下面输入框，Enter 发送，Shift+Enter 换行。没选 agent 时输入框是灰的。

**收回复：** agent 的文字支持 Markdown（列表、链接、代码块等）。思考中会显示 Thinking 或 thinking 卡片；如果 agent 调了工具，会看到 tool 名称、参数、结果，可以点开看。

**连接状态（对话框上方）：** 有个带颜色小圆点的状态条——绿的是 Connected，琥珀色 Reconnecting，灰的是 Disconnected，后面还会跟 idle / working / working 这类 agent 状态。如果断开了，会出现 **Reconnect** 按钮，点一下走 SDK 重连；重连时按钮会转圈。连上了之后，之前那种过期的 “Reconnect timed out” 红字不会一直挂着误导人。

**长回复：** agent 回答特别长（大约七百字以上）可以 **Collapse / Expand** 折叠展开，默认是展开的。

**复制：** agent 回复可以 Copy，hover 或下面有按钮，复制完会短暂显示 Copied。

**滚屏：** 你在底部看新消息时会自动滚到底；如果你往上翻历史，不会被强行拽回去。

**空对话：** 新 session 没消息时，输入框上面有几个小建议问题，点一下会填进输入框。

**模式：** 输入框下面有个很小的 Mode 下拉（Safe / Plan 等），是 SDK 带的 approval 模式，一般 demo 用默认 Safe 就行。

**直连提示：** 如果是 Direct URL 连的，输入框上方会有一行小字显示 `Direct · http://...`。

**恢复历史：** 关掉 App 再开，之前的聊天记录会从本地读回来。切 session 时有时会先显示 **Loading conversation…**，等 SQLite 里那段的 SDK 状态加载完。

---

## 深色模式和设置

**深色模式**大致做完了。View 菜单里可以切 Dark mode，Settings → Appearance 里也能切 Light/Dark，两处是同一个开关。聊天正文、Add Agent 表单、输入框、各种活动卡片在深色下都能看清字（我们专门修过一轮）。

**Settings 页：** 侧栏底部 Settings 进去。除了主题，还能调 **消息字号** S/M/L。还有 **Keyboard shortcuts**：默认 **Ctrl+B** 隐藏/显示侧栏，**Ctrl+N** 当前 agent 新开聊；可以点某一行重新录按键，Esc 取消，Reset 恢复默认。Help 菜单里也有项跳到快捷键这一块。

**隐藏侧栏：** View → Toggle sidebar，或 Ctrl+B。藏掉之后聊天区全宽，设置会记住，下次打开还是那样。

---

## 数据存在哪、为什么有时 Loading

以前全在浏览器 localStorage 里，现在 Electron 下**主要用 SQLite**，文件在：

`%APPDATA%\connectonion_desktop\storage\connectonion-desktop.sqlite3`

里面存 agent 列表、session 列表、聊天内容（SDK 那份 state）、主题侧栏字号快捷键等。启动 App 时会先从硬盘读到内存，所以刚打开侧栏可能短暂显示 **Loading agents…**，聊天区也可能 **Loading…**，正常，一般很快。

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

右边是最小化、最大化、关闭。开发模式下会自动开 DevTools，打包正式版不会。

输入框左边有个**回形针**图标，**还没有附件功能**，先别点着期待能传文件。

删 agent 或 session 前会 **confirm 确认框**，防手滑。

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

现在这个客户端已经能：侧栏管 agent 和 session、测连接、本地或 relay 聊天、SQLite 存记录、深色模式、重连、复制和折叠长回复、自定义快捷键。你们 pull 下来按上面跑 simple-agent + desktop 就能试；有问题看 `HANDOFF_CONTEXT.md` 或直接问我和 A。
