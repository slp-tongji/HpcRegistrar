using System.Buffers;
using System.Diagnostics;
using System.IO;

namespace TongjiHpcRegistrar.Services;

public static class SpaceFilesCreator
{
    public static async Task EnsureAsync(SshNetHpcClient hpc, SpaceLayout layout, CancellationToken cancellationToken = default)
    {
        if (await hpc.PathExistsAsync(layout.SpaceHomePath, cancellationToken))
        {
            return;
        }

        var temporary = layout.SpaceHomePath + ".tmp." + Guid.NewGuid().ToString("N");

        await hpc.CreateDirectoryAsync(layout.SpaceSsdfsPath, cancellationToken);
        await WriteHomeAsync(hpc, temporary, layout, cancellationToken);

        await hpc.MoveAsync(temporary, layout.SpaceHomePath, cancellationToken);
    }

    private static async Task WriteHomeAsync(
        SshNetHpcClient hpc, string path, SpaceLayout layout, CancellationToken cancellationToken
    )
    {
        await hpc.CreateDirectoryAsync(path, cancellationToken);

        await WriteHpcRegistrarDirectoryAsync(
            hpc, Path.Combine(path, layout.HpcRegistrarDirectoryName), layout, cancellationToken
        );
        await WriteSshDirectoryAsync(hpc, layout, Path.Combine(path, ".ssh"), cancellationToken);

        await hpc.CreateSymbolicLinkAsync(Path.Combine(path, "ssdfs"), layout.SpaceSsdfsPath, cancellationToken);
        await hpc.WriteFileAsync(
            Path.Combine(path, ".bashrc"),
            $$"""
            # ~/.bashrc

            if [ -f /etc/bashrc ]; then
                . /etc/bashrc
            fi

            if ! [[ "$PATH" =~ "$HOME/.local/bin:$HOME/bin:" ]]
            then
                PATH="$HOME/.local/bin:$HOME/bin:$PATH"
            fi
            export PATH

            # ===== Hpc Registrar =====
            # 隔离空间通过修改环境变量实现，但部分程序不尊重 HOME 变量。
            # 此处对已知不兼容、且容易通过环境变量修复的程序做兜底配置。
            # 注意：此文件在空间首次创建时生成，之后不会再更新；若后续遇到其他不兼容的程序，需手动添加。
            # 更多信息请参考 https://github.com/slp-tongji/TongjiHpcRegistrar-Documentation

            # tmux
            export TMUX_TMPDIR="$XDG_RUNTIME_DIR"/tmux-{{Escape(layout.SpaceName)}}
            /usr/bin/mkdir -p "$TMUX_TMPDIR"

            # screen
            export SCREENDIR="$XDG_RUNTIME_DIR"/screen-{{Escape(layout.SpaceName)}}

            # git
            export GIT_SSH_COMMAND="/usr/bin/ssh -F $HOME/.ssh/config"
            # ===== Hpc Registrar =====

            # User specific aliases and functions

            if [[ $- == *i* ]]; then
                echo "欢迎！如果看到了这条消息，说明已成功配置隔离空间！（可以在 ~/.bashrc 中移除这条提示）"
                echo "注意：隔离空间通过修改环境变量实现，部分程序可能不完全兼容，已知不兼容的有："
                echo "  - ssh：默认读取 passwd 而不尊重 HOME 变量；直接使用 ssh 时需手动指定配置，但 git 已通过 GIT_SSH_COMMAND 配好"
                echo "  - slurm：若配置了清除环境变量等行为，可能读取 passwd 获取 HOME；一般情况下没有问题"
                echo "  - cron：服务运行在系统级别，无法简单隔离；尽量使用绝对路径"
                echo "  - systemd：服务由系统 systemd 拉起，无法简单隔离；配置需在原本家目录下进行，尽量使用绝对路径"
                echo "更多信息请参考 https://github.com/slp-tongji/TongjiHpcRegistrar-Documentation"
            fi
            """,
            cancellationToken);
        await hpc.WriteFileAsync(
            Path.Combine(path, ".bash_profile"),
            """
            # ~/.bash_profile

            if [ -f ~/.bashrc ]; then
                . ~/.bashrc
            fi

            # User specific environment and startup programs
            """,
            cancellationToken
        );
        await hpc.WriteFileAsync(
            Path.Combine(path, ".bash_logout"),
            """
            # ~/.bash_logout
            """,
            cancellationToken
        );
    }

    private static async Task WriteHpcRegistrarDirectoryAsync(
        SshNetHpcClient hpc, string path, SpaceLayout layout, CancellationToken cancellationToken
    )
    {
        await hpc.CreateDirectoryAsync(path, cancellationToken);

        var commandFile = Path.Combine(path, layout.SshCommandFileName);
        await hpc.WriteFileAsync(
            commandFile,
            $$"""
            #!/usr/bin/bash

            # 本文件与 Tongji Hpc Registrar 相关，请勿修改、删除或移动，这可能导致无法正常登录
            # 更多信息请参考 https://github.com/slp-tongji/TongjiHpcRegistrar-Documentation

            NEW_HOME={{Escape(layout.SpaceHomePath)}}

            NEW_ENV=("HOME=$NEW_HOME" "SHELL=/usr/bin/bash")

            add_env() {
                if [ -n "${!1}" ]; then
                    NEW_ENV+=("$1=${!1}")
                fi
            }

            add_env TERM
            add_env SSH_AUTH_SOCK
            add_env XDG_RUNTIME_DIR
            add_env DBUS_SESSION_BUS_ADDRESS
            add_env DISPLAY
            add_env XAUTHORITY
            add_env XDG_SESSION_ID
            add_env XDG_SESSION_TYPE
            add_env XDG_SESSION_CLASS
            add_env SSH_CONNECTION
            add_env SSH_CLIENT
            add_env SSH_TTY

            cd "$NEW_HOME"

            if [ -z "$SSH_ORIGINAL_COMMAND" ]; then
                exec /usr/bin/env -i "${NEW_ENV[@]}" /usr/bin/bash --login
            else
                exec /usr/bin/env -i "${NEW_ENV[@]}" /usr/bin/bash --login -c "$SSH_ORIGINAL_COMMAND"
            fi
            """,
            cancellationToken);
        await hpc.ChmodAsync(commandFile, UnixFileMode.UserRead | UnixFileMode.UserExecute, cancellationToken);

        var ownerFile = Path.Combine(path, "owner");
        await hpc.WriteFileAsync(
            ownerFile,
            $$"""
            # 本文件用以指定此隔离空间的所有者，请不要修改或删除
            {{layout.Sub}}
            """,
            cancellationToken);
        await hpc.ChmodAsync(ownerFile, UnixFileMode.UserRead, cancellationToken);

        await hpc.ChmodAsync(path, UnixFileMode.UserRead | UnixFileMode.UserExecute, cancellationToken);
    }

    private static async Task WriteSshDirectoryAsync(
        SshNetHpcClient hpc, SpaceLayout layout, string path, CancellationToken cancellationToken
    )
    {
        await hpc.CreateDirectoryAsync(path, cancellationToken);

        var keyFile = Path.Combine(path, "id_ed25519");
        await hpc.GenerateSshKeyAsync(keyFile, cancellationToken);

        await hpc.WriteFileAsync(
            Path.Combine(path, "authorized_keys"),
            $$"""
            # 请注意，本文件位于隔离空间中，不会在登录时起到作用。
            # 更多信息请参考 https://github.com/slp-tongji/TongjiHpcRegistrar-Documentation
            """,
            cancellationToken);
        await hpc.WriteFileAsync(
            Path.Combine(path, "config"),
            $$"""
            # 请注意，SSH 不尊重 HOME 环境变量，因此本配置默认不会被使用
            # git 已通过 GIT_SSH_COMMAND 自动应用本配置（见 ~/.bashrc）
            # 直接使用 ssh 时，需手动 ssh -F "$HOME/.ssh/config" 以应用此配置
            # 更多信息请参考 https://github.com/slp-tongji/TongjiHpcRegistrar-Documentation

            Host *
                IdentityFile "{{EscapeSshConfigValue(Path.Combine(layout.SpaceHomePath, ".ssh", "id_ed25519"))}}"
                UserKnownHostsFile "{{EscapeSshConfigValue(Path.Combine(layout.SpaceHomePath, ".ssh", "known_hosts"))}}"

            # 通过 443 端口访问 GitHub，规避 22 端口被屏蔽的情况
            Host github.com
                Hostname ssh.github.com
                Port 443
            """,
            cancellationToken);
    }

    private static readonly SearchValues<char> safeCharacters = SearchValues.Create(
        "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_@%+=:,./-"
    );
    private static string Escape(string token)
    {
        if (token == "")
            return "''";
        if (token.AsSpan().ContainsAnyExcept(safeCharacters))
            return $"'{token.Replace("'", "'\"'\"'")}'";
        return token;
    }

    private static string EscapeSshConfigValue(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
