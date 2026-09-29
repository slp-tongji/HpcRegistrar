using System.Buffers;
using System.Diagnostics;

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

            # User specific aliases and functions

            # ===== tmux =====
            # https://github.com/slp-tongji/TongjiHpcRegistrar-Documentation
            export TMUX_TMPDIR="/tmp/tmux-$(id -u)/"{{Escape(layout.SpaceName)}}
            mkdir -p "$TMUX_TMPDIR"
            # ===== tmux =====

            echo "欢迎！如果看到了这条消息，说明已成功配置隔离空间！（可以在 ~/.bashrc 中移除这条提示）"
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
            #!/bin/bash

            # 本文件与 Tongji Hpc Registrar 相关，请勿修改、删除或移动，这可能导致无法正常登录
            # 更多信息请参考 https://github.com/slp-tongji/TongjiHpcRegistrar-Documentation

            NEW_HOME={{Escape(layout.SpaceHomePath)}}
            NEW_ENV="HOME=$NEW_HOME TERM=$TERM SSH_AUTH_SOCK=$SSH_AUTH_SOCK"

            cd $NEW_HOME

            if [ -z "$SSH_ORIGINAL_COMMAND" ]; then
                exec env -i $NEW_ENV /bin/bash --login
            else
                exec env -i $NEW_ENV /bin/bash --login -c "$SSH_ORIGINAL_COMMAND"
            fi
            """,
            cancellationToken);
        await hpc.SetExecutableAsync(commandFile, cancellationToken);

        await hpc.WriteFileAsync(
            Path.Combine(path, "owner"),
            $$"""
            # 本文件用以指定此隔离空间的所有者，请不要修改或删除
            {{layout.Sub}}
            """,
            cancellationToken);
    }

    private static async Task WriteSshDirectoryAsync(
        SshNetHpcClient hpc, SpaceLayout layout, string path, CancellationToken cancellationToken
    )
    {
        await hpc.CreateDirectoryAsync(path, cancellationToken);

        await hpc.WriteFileAsync(
            Path.Combine(path, "authorized_keys"),
            $$"""
            # 请注意，本文件位于隔离空间中，不会在登录时起到作用。
            """,
            cancellationToken);
        await hpc.WriteFileAsync(
            Path.Combine(path, "config"),
            $$"""
            # 请注意， SSH 不尊重 HOME 环境变量，因此本配置默认不会被使用。
            # 如果需要，可使用 ssh -F "$HOME/.ssh/config" 以应用此配置。
            # 注意此目录下的 SSH 密钥也不会被自动取用，需要手动指定。
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
}
