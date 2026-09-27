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

    public string Owner => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    public string UserName => User.FindFirstValue(ClaimTypes.Name) ?? Owner;

    public string Title => appOptions.Title;

    public IReadOnlyList<SpaceOwnership> Spaces { get; private set; } = [];

    public string? Error { get; private set; }

    public string? Success { get; private set; }

    public IndexModel(AppOptions appOptions, IsolationSpaceService spaceService)
    {
        this.appOptions = appOptions;
        this.spaceService = spaceService;
    }

    public void OnGet() => Spaces = [.. spaceService.ListOwn(Owner)];

    public async Task<IActionResult> OnPostCreateAsync(string name, string contact, string key, CancellationToken cancellationToken)
    {
        Error = await spaceService.CreateAsync(name, contact, key, Owner, cancellationToken);
        Success = Error is null ? "隔离空间已创建。" : null;
        Spaces = [.. spaceService.ListOwn(Owner)];
        return Page();
    }

    public async Task<IActionResult> OnPostDeleteAsync(string name, CancellationToken cancellationToken)
    {
        var deleted = await spaceService.DeleteOwnAsync(name, Owner, cancellationToken);
        Error = deleted ? null : "删除失败：没有权限，或该空间不存在。";
        Success = deleted ? "隔离空间已删除。" : null;
        Spaces = [.. spaceService.ListOwn(Owner)];
        return Page();
    }
}
