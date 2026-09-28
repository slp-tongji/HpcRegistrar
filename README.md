# Tongji Hpc Registrar

为 HPC 集群提供「隔离空间」的注册服务。用户通过 OIDC（如 Dex + GitHub）登录后，在网页上添加/删除自己的 SSH 公钥；注册器以固定服务账号身份 SSH 到 HPC，维护 `authorized_keys` 并为每个用户创建隔离空间。

## 工作原理

- 每个 OIDC 用户（由 `sub` 标识）对应一个隔离空间，空间名固定为 `s` + `SHA256(sub)` 前 8 字节的十六进制小写，例如 `s1a2b3c4d5e6f7a8`。
- 注册器在 `authorized_keys` 中写入形如
  `command="/share/home/<user>/data/<space>/.hpc-isolation/ssh-command.sh" ssh-ed25519 AAAA...` 的行。
- `command=` 强制该公钥登录后只执行 `ssh-command.sh`，它把 `HOME` 切到隔离空间并启动一个干净的 bash，从而与原始 home 隔离。
- `authorized_keys` 是唯一的事实来源，不额外维护数据库。

目录布局（硬编码于 `SpaceLayout`）：

| 路径 | 含义 |
| --- | --- |
| `/share/home/<user>` | 服务账号的原始 home |
| `/share/home/<user>/data/<space>` | 隔离空间 |
| `/ssdfs/datahome/<user>/<space>` | 挂载为空间内 `ssdfs` 符号链接 |
| `/share/home/<user>/.ssh/authorized_keys` | 唯一事实来源 |

## 构建

需要 .NET 10 SDK（`flake.nix` 已提供）：

```sh
nix develop --command dotnet build
```

## 运行

```sh
nix develop --command dotnet run -- \
  --listen http://0.0.0.0:8080 \
  --oidc https://dex.example.com \
  --oidc-id <client-id> \
  --oidc-secret <client-secret> \
  --hpc-host hpc.example.com \
  --hpc-port 22 \
  --hpc-user <service-account> \
  --hpc-key /path/to/private_key \
  --hpc-host-key <fingerprint>
```

### 命令行参数

| 参数 | 说明 |
| --- | --- |
| `--listen` | 监听地址（传给 ASP.NET Core 的 `--urls`） |
| `--oidc` | OIDC authority（如 Dex） |
| `--oidc-id` | OIDC client id |
| `--oidc-secret` | OIDC client secret；也可通过环境变量 `TONGJI_HPC_REGISTRAR_ARGUMENT_OIDC_SECRET` 提供 |
| `--oidc-ca` | 可选，OIDC 的 CA 证书（PEM），用于信任自签名证书 |
| `--hpc-host` | HPC 主机名 |
| `--hpc-port` | HPC SSH 端口 |
| `--hpc-user` | 用于 SSH 的服务账号用户名 |
| `--hpc-key` | SSH 私钥路径 |
| `--hpc-host-key` | HPC 主机密钥指纹（见下） |

### 部署前提

1. **`.ssh` 目录与 `authorized_keys` 必须预先创建**。注册器假设 `/share/home/<user>/.ssh/authorized_keys` 已存在；若不存在，添加/删除会失败。文件权限 644（sshd 的 `StrictModes` 可接受，如需 0600 请在部署侧设置）。
2. **`--hpc-host-key` 格式**：必须大写、去掉 `SHA256:` 前缀（与 SSH.NET 的 `FingerPrintSHA256` 逐字节比较）。可从 `ssh-keyscan` 或 `ssh-keygen -lf` 的结果中提取。
3. **`--hpc-user` 只能包含 `[a-zA-Z0-9._-]` 且不能为空**。它会被用作 SSH 用户名、文件系统路径，并嵌入 `authorized_keys` 的 `command=` 选项（该值最终由 `shell -c` 执行），特殊字符会破坏这些格式甚至导致命令注入。不满足时启动即报错。

## 安全说明

- 公钥经 `SshPublicKeyParser.Normalize` 严格校验：按空白分词、白名单密钥类型、base64 字符白名单，杜绝换行/注释/特殊字符注入。
- `command=` 的值经白名单校验（`[a-zA-Z0-9._-]`），防止 shell 元字符注入与路径逃逸。
- 删除公钥用 `grep -vxF` 整行精确匹配，前缀含唯一的空间路径，避免越权删他人行。
- 前端启用 antiforgery token（CSRF 防护），Razor 默认 HTML 编码（XSS 防护）。
