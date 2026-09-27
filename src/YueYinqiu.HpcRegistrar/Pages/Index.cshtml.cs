using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using YueYinqiu.HpcRegistrar.Services;

namespace YueYinqiu.HpcRegistrar.Pages;

[Authorize]
public sealed class IndexModel : PageModel
{
    private readonly IsolationSpaceService spaceService;

    public string Owner
    {
        get
        {
            var result = User.FindFirstValue(ClaimTypes.NameIdentifier);
            Trace.Assert(result is not null);
            return result;
        }
    }

    public string UserName => User.FindFirstValue("name") ?? Owner;

    public IReadOnlyList<AuthorizedKey> Keys { get; private set; } = [];

    public string? Error { get; private set; }

    public string? Success { get; private set; }

    public IndexModel(IsolationSpaceService spaceService)
    {
        this.spaceService = spaceService;
    }

    public async Task OnGetAsync(CancellationToken cancellationToken) =>
        Keys = await spaceService.ListKeysAsync(Owner, cancellationToken);

    public async Task<IActionResult> OnPostAddKeyAsync(string key, CancellationToken cancellationToken)
    {
        Error = await spaceService.AddKeyAsync(Owner, UserName, key, cancellationToken);
        Success = Error is null ? "公钥已添加。" : null;
        Keys = await spaceService.ListKeysAsync(Owner, cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostRemoveKeyAsync(string fingerprint, CancellationToken cancellationToken)
    {
        var removed = await spaceService.RemoveKeyAsync(Owner, fingerprint, cancellationToken);
        Error = removed ? null : "删除失败：该公钥不存在。";
        Success = removed ? "公钥已删除。" : null;
        Keys = await spaceService.ListKeysAsync(Owner, cancellationToken);
        return Page();
    }
}
