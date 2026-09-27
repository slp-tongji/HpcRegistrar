namespace YueYinqiu.HpcRegistrar.Services;

public sealed class SpaceLayout
{
    public const string HomeRoot = "/share/home";

    public const string SsdfsDataHome = "/ssdfs/datahome";

    public string Name { get; }

    public string Username { get; }

    public string Home { get; }

    public string Space { get; }

    public string Ssdfs { get; }

    public string SshCommand { get; }

    public string AuthorizedKeys { get; }

    public SpaceLayout(string username, string name)
    {
        Name = name;
        Username = username;
        Home = Path.Combine(HomeRoot, Username);
        Space = Path.Combine(Home, "data", name);
        Ssdfs = Path.Combine(SsdfsDataHome, Username, name);
        SshCommand = Path.Combine(Space, ".ssh-command");
        AuthorizedKeys = Path.Combine(Home, ".ssh", "authorized_keys");
    }

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
