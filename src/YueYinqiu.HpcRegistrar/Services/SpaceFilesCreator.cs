namespace YueYinqiu.HpcRegistrar.Services;

public static class SpaceFilesCreator
{
    public static async Task EnsureAsync(SshNetHpcClient hpc, SpaceLayout layout, CancellationToken cancellationToken = default)
    {
        if (await hpc.PathExistsAsync(layout.SpaceHomePath, cancellationToken)
            && !await hpc.PathExistsAsync(layout.BrokenMarkerPath, cancellationToken))
        {
            return;
        }

        await hpc.WriteFileAsync(layout.BrokenMarkerPath, "", cancellationToken);
        await hpc.CreateDirectoryAsync(layout.SpaceHomePath, cancellationToken);

        await WriteSshCommandAsync(hpc, layout, cancellationToken);
        await WriteBashLogoutAsync(hpc, layout, cancellationToken);
        await WriteBashProfileAsync(hpc, layout, cancellationToken);
        await WriteBashrcAsync(hpc, layout, cancellationToken);
        await WriteSpaceSshAsync(hpc, layout, cancellationToken);
        await CreateSsdfsAsync(hpc, layout, cancellationToken);

        await hpc.RemoveFileAsync(layout.BrokenMarkerPath, cancellationToken);
    }

    private static async Task WriteSshCommandAsync(SshNetHpcClient hpc, SpaceLayout layout, CancellationToken cancellationToken)
    {
        await hpc.WriteFileAsync(
            layout.SshCommandPath,
            $$"""
            #!/bin/bash

            NEW_HOME="{{layout.SpaceHomePath}}"
            NEW_ENV="HOME=$NEW_HOME TERM=$TERM SSH_AUTH_SOCK=$SSH_AUTH_SOCK"

            cd $NEW_HOME

            if [ -z "$SSH_ORIGINAL_COMMAND" ]; then
                exec env -i $NEW_ENV /bin/bash --login
            else
                exec env -i $NEW_ENV /bin/bash --login -c "$SSH_ORIGINAL_COMMAND"
            fi
            """,
            cancellationToken);
        await hpc.SetExecutableAsync(layout.SshCommandPath, cancellationToken);
    }

    private static async Task WriteBashLogoutAsync(SshNetHpcClient hpc, SpaceLayout layout, CancellationToken cancellationToken) =>
        await hpc.WriteFileAsync(
            Path.Combine(layout.SpaceHomePath, ".bash_logout"),
            """
            # ~/.bash_logout
            """,
            cancellationToken);

    private static async Task WriteBashProfileAsync(SshNetHpcClient hpc, SpaceLayout layout, CancellationToken cancellationToken) =>
        await hpc.WriteFileAsync(
            Path.Combine(layout.SpaceHomePath, ".bash_profile"),
            """
            # ~/.bash_profile

            if [ -f ~/.bashrc ]; then
                . ~/.bashrc
            fi

            # User specific environment and startup programs
            """,
            cancellationToken);

    private static async Task WriteBashrcAsync(SshNetHpcClient hpc, SpaceLayout layout, CancellationToken cancellationToken) =>
        await hpc.WriteFileAsync(
            Path.Combine(layout.SpaceHomePath, ".bashrc"),
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
            export HOME_ORIGINAL="{{layout.OriginalHomePath}}"

            # ===== tmux =====
            # https://tjslp-hpc.yueyinqiu.top/docs/quick-start/create-isolation-space/#tmux-%e4%b8%8d%e5%85%bc%e5%ae%b9
            export TMUX_TMPDIR="$HOME/.tmux/tmp"
            mkdir -p "$TMUX_TMPDIR"
            # ===== tmux =====

            echo "欢迎！如果看到了这条消息，说明已成功配置隔离空间！（可以在 ~/.bashrc 中移除这条提示）"
            """,
            cancellationToken);

    private static async Task WriteSpaceSshAsync(SshNetHpcClient hpc, SpaceLayout layout, CancellationToken cancellationToken)
    {
        var spaceSsh = Path.Combine(layout.SpaceHomePath, ".ssh");
        await hpc.CreateDirectoryAsync(spaceSsh, cancellationToken);
        await hpc.WriteFileAsync(
            Path.Combine(spaceSsh, "authorized_keys"),
            $$"""
            # 请注意，本文件位于隔离空间中，不会在登录时起到作用。
            # 若要配置 authorized_keys ，应该使用 {{layout.OriginalAuthorizedKeysPath}}
            """,
            cancellationToken);
        await hpc.WriteFileAsync(
            Path.Combine(spaceSsh, "config"),
            """
            # 请注意，本配置默认不会被使用。
            # 详见 https://tjslp-hpc.yueyinqiu.top/docs/quick-start/create-isolation-space/#ssh-%e4%b8%8d%e5%85%bc%e5%ae%b9
            """,
            cancellationToken);
    }

    private static async Task CreateSsdfsAsync(SshNetHpcClient hpc, SpaceLayout layout, CancellationToken cancellationToken)
    {
        await hpc.CreateDirectoryAsync(layout.SpaceSsdfsPath, cancellationToken);
        await hpc.CreateSymbolicLinkAsync(Path.Combine(layout.SpaceHomePath, "ssdfs"), layout.SpaceSsdfsPath, cancellationToken);
    }
}
