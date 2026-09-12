# 部署清单：HTTP 端口权限（URL ACL）

MainAPP 内嵌两个 `HttpListener` 宿主，监听 `http://+:端口/`（供手机浏览器访问）：

| 宿主 | 端口 | 用途 |
|---|---|---|
| `CameraWebHost` | 5188 | 相机远程看图（`/` 静态页、`/mjpeg`、`/api/status`）|
| `AiWebHost` | 5190 | AI 助手对话面板（Deep Chat）|

## 为什么需要

`http://+:端口/` 这种"绑定全部网卡"的 URL 需要 Windows URL ACL 授权；未授权时监听会抛
「拒绝访问」并**静默回退 `http://127.0.0.1:端口/`**——程序不报错，但手机/其他电脑访问不到。

## 一次性安装（每台产线机器执行一次）

以**管理员身份**运行仓库根目录的：

```
scripts\install-urlacl.cmd
```

其内容等价于：

```
netsh http add urlacl url=http://+:5188/ user=Everyone
netsh http add urlacl url=http://+:5190/ user=Everyone
```

## 验证

```
netsh http show urlacl
```

应能看到 5188/5190 两条记录。然后在手机浏览器访问 `http://<本机IP>:5188/` 与 `:5190/` 确认可达。

## 备注

- 不想加 ACL 的替代方案：仅本机使用时无需任何配置（自动回退 127.0.0.1 即可）。
- 防火墙如拦截入站，还需放行 5188/5190 入站规则（`netsh advfirewall firewall add rule ...`）。
