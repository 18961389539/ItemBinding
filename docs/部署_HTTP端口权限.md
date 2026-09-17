# 部署清单：HTTP 端口权限（URL ACL）

MainAPP 内嵌三个 `HttpListener` 宿主，监听 `http://+:端口/`（供手机/其他电脑浏览器访问）：

| 宿主 | 端口 | 用途 |
|---|---|---|
| `CameraWebHost` | 5188 | 相机远程看图（`/` 静态页、`/mjpeg`、`/api/status`）|
| `AiWebHost` | 5190 | AI 助手对话面板（Deep Chat）|
| `OpsWebHost` | 5191 | 统一远程运维门户（相机/读码器、编码器/VGT、系统状态、最近告警、快捷操作）|
| `RecipeTcpServerService` | 5000 | 配方切换 TCP（文本协议 `SWITCH_RECIPE` / `GET_RECIPE` / `LIST_RECIPES`），**不使用 URL ACL**（原生 socket，非 HttpListener）|

## 为什么需要

`http://+:端口/` 这种"绑定全部网卡"的 URL 需要 Windows URL ACL 授权；未授权时监听会抛
「拒绝访问」并**静默回退 `http://127.0.0.1:端口/`**——程序不报错，但手机/其他电脑访问不到。

> ⚠️ 2026-09-16 修正：本清单与 `scripts\install-urlacl.cmd` 此前**只写了 5188/5190，漏了 5191**。
> 后果是照文档做的现场，运维门户（5191）始终只绑在本机回环上，手机打不开而文档又是"按步骤做完"的样子，
> 极难自查。请务必按下面的完整清单核对。

## 一次性安装（每台产线机器执行一次）

以**管理员身份**运行仓库根目录的：

```
scripts\install-urlacl.cmd
```

其内容等价于：

```
netsh http add urlacl url=http://+:5188/ user=Everyone
netsh http add urlacl url=http://+:5190/ user=Everyone
netsh http add urlacl url=http://+:5191/ user=Everyone
```

## 验证

```
netsh http show urlacl
```

应能看到 **5188 / 5190 / 5191 三条**记录。然后在手机浏览器访问
`http://<本机IP>:5188/`、`:5190/`、`:5191/` 确认三个页面都可达。

程序侧也可自查：启动日志里若出现"仅绑定本机回环"的 Warning，说明该端口缺 ACL；正常应为
`http://+:端口/`（例如 `[运维门户] 端口 5191 已启动`）。

## 备注

- 不想加 ACL 的替代方案：仅本机使用时无需任何配置（自动回退 127.0.0.1 即可）。
- 防火墙如拦截入站，还需放行 5188/5190/5191 的入站规则（`netsh advfirewall firewall add rule ...`）。
