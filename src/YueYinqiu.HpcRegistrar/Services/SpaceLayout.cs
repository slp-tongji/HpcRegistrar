using System.Security.Cryptography;
using System.Text;

namespace YueYinqiu.HpcRegistrar.Services;

public sealed class SpaceLayout(string username, string sub)
{
    private const string shareHome = "/share/home";

    private const string ssdfsDatahome = "/ssdfs/datahome";

    public string Name { get; } = "s" + Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(sub)).AsSpan(0, 8)).ToLowerInvariant();

    public string Username => username;

    public string Home => Path.Combine(shareHome, username);

    public string Space => Path.Combine(Home, "data", Name);

    public string Ssdfs => Path.Combine(ssdfsDatahome, username, Name);

    public string SshCommand => Path.Combine(Space, ".hpc-isolation", "ssh-command.sh");

    public string AuthorizedKeys => Path.Combine(Home, ".ssh", "authorized_keys");

    public string SshCommandContent() =>
        $$"""
        #!/bin/bash

        NEW_HOME="{{Space}}"
        NEW_ENV="HOME=$NEW_HOME TERM=$TERM SSH_AUTH_SOCK=$SSH_AUTH_SOCK"

        cd $NEW_HOME

        if [ -z "$SSH_ORIGINAL_COMMAND" ]; then
            exec env -i $NEW_ENV /bin/bash --login
        else
            exec env -i $NEW_ENV /bin/bash --login -c "$SSH_ORIGINAL_COMMAND"
        fi
        """;

    public string BashLogoutContent() =>
        """
        # ~/.bash_logout
        """;

    public string BashProfileContent() =>
        """
        # ~/.bash_profile

        if [ -f ~/.bashrc ]; then
            . ~/.bashrc
        fi

        # User specific environment and startup programs
        """;

    public string BashrcContent() =>
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
        export HOME_ORIGINAL="{{Home}}"

        # ===== tmux =====
        # https://tjslp-hpc.yueyinqiu.top/docs/quick-start/create-isolation-space/#tmux-%e4%b8%8d%e5%85%bc%e5%ae%b9
        export TMUX_TMPDIR="$HOME/.tmux/tmp"
        mkdir -p "$TMUX_TMPDIR"
        # ===== tmux =====

        echo "欢迎！如果看到了这条消息，说明已成功配置隔离空间！（可以在 ~/.bashrc 中移除这条提示）"
        """;

    public string SpaceAuthorizedKeysContent() =>
        $$"""
        # 请注意，本文件位于隔离空间中，不会在登录时起到作用。
        # 若要配置 authorized_keys ，应该使用 {{AuthorizedKeys}}
        """;

    public string SpaceSshConfigContent() =>
        """
        # 请注意，本配置默认不会被使用。
        # 详见 https://tjslp-hpc.yueyinqiu.top/docs/quick-start/create-isolation-space/#ssh-%e4%b8%8d%e5%85%bc%e5%ae%b9
        """;

    public string AuthorizedKeyLine(string key, string comment) =>
        $"command=\"{SshCommand}\" {key} {comment}";
}
