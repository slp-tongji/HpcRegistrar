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
    private readonly AppOptions appOptions;
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

    public string Title => appOptions.Title;

    public SpaceOwnership? Space { get; private set; }

    public string? Error { get; private set; }

    public string? Success { get; private set; }

    public IndexModel(AppOptions appOptions, IsolationSpaceService spaceService)
    {
        this.appOptions = appOptions;
        this.spaceService = spaceService;
    }

    public void OnGet() => Space = spaceService.FindOwn(Owner);

    public async Task<IActionResult> OnPostAddKeyAsync(string key, CancellationToken cancellationToken)
    {
        Error = await spaceService.AddKeyAsync(Owner, UserName, key, cancellationToken);
        Success = Error is null ? "公钥已添加。" : null;
        Space = spaceService.FindOwn(Owner);
        return Page();
    }

    public async Task<IActionResult> OnPostRemoveKeyAsync(string fingerprint, CancellationToken cancellationToken)
    {
        var removed = await spaceService.RemoveKeyAsync(Owner, fingerprint, cancellationToken);
        Error = removed ? null : "删除失败：该公钥不存在。";
        Success = removed ? "公钥已删除。" : null;
        Space = spaceService.FindOwn(Owner);
        return Page();
    }
}
