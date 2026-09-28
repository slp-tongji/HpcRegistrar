namespace YueYinqiu.HpcRegistrar.Services;

public static class SpaceFilesCreator
{
    public static async Task EnsureAsync(SshNetHpcClient hpc, SpaceLayout layout, CancellationToken cancellationToken = default)
    {
        if (await hpc.PathExistsAsync(layout.SpaceHomePath, cancellationToken))
        {
            return;
        }

        var tempRoot = layout.SpaceHomePath + ".tmp." + Guid.NewGuid().ToString("N");
        await hpc.CreateDirectoryAsync(tempRoot, cancellationToken);

        await WriteSshCommandAsync(hpc, layout, tempRoot, cancellationToken);
        await WriteBashLogoutAsync(hpc, layout, tempRoot, cancellationToken);
        await WriteBashProfileAsync(hpc, layout, tempRoot, cancellationToken);
        await WriteBashrcAsync(hpc, layout, tempRoot, cancellationToken);
        await WriteSpaceSshAsync(hpc, layout, tempRoot, cancellationToken);
        await CreateSsdfsAsync(hpc, layout, tempRoot, cancellationToken);

        await hpc.MoveAsync(tempRoot, layout.SpaceHomePath, cancellationToken);
    }

    private static async Task WriteSshCommandAsync(SshNetHpcClient hpc, SpaceLayout layout, string targetRoot, CancellationToken cancellationToken)
    {
        var target = Path.Combine(targetRoot, layout.SshCommandRelativePath);
        await hpc.CreateDirectoryAsync(Path.GetDirectoryName(target)!, cancellationToken);
        await hpc.WriteFileAsync(
            target,
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
        await hpc.SetExecutableAsync(target, cancellationToken);
    }

    private static async Task WriteBashLogoutAsync(SshNetHpcClient hpc, SpaceLayout layout, string targetRoot, CancellationToken cancellationToken) =>
        await hpc.WriteFileAsync(
            Path.Combine(targetRoot, ".bash_logout"),
            """
            # ~/.bash_logout
            """,
            cancellationToken);

    private static async Task WriteBashProfileAsync(SshNetHpcClient hpc, SpaceLayout layout, string targetRoot, CancellationToken cancellationToken) =>
        await hpc.WriteFileAsync(
            Path.Combine(targetRoot, ".bash_profile"),
            """
            # ~/.bash_profile

            if [ -f ~/.bashrc ]; then
                . ~/.bashrc
            fi

            # User specific environment and startup programs
            """,
            cancellationToken);

    private static async Task WriteBashrcAsync(SshNetHpcClient hpc, SpaceLayout layout, string targetRoot, CancellationToken cancellationToken) =>
        await hpc.WriteFileAsync(
            Path.Combine(targetRoot, ".bashrc"),
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

    private static async Task WriteSpaceSshAsync(SshNetHpcClient hpc, SpaceLayout layout, string targetRoot, CancellationToken cancellationToken)
    {
        var spaceSsh = Path.Combine(targetRoot, ".ssh");
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

    private static async Task CreateSsdfsAsync(SshNetHpcClient hpc, SpaceLayout layout, string targetRoot, CancellationToken cancellationToken)
    {
        await hpc.CreateDirectoryAsync(layout.SpaceSsdfsPath, cancellationToken);
        await hpc.CreateSymbolicLinkAsync(Path.Combine(targetRoot, "ssdfs"), layout.SpaceSsdfsPath, cancellationToken);
    }
}
