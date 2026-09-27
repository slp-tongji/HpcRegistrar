using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using YueYinqiu.HpcRegistrar.Services;

namespace YueYinqiu.HpcRegistrar.Pages;

[Authorize(Policy = "Administrator")]
public sealed class AdministrationModel : PageModel
{
    private readonly AppOptions appOptions;
    private readonly IsolationSpaceService spaceService;

    public string Title => appOptions.Title;

    public IReadOnlyList<SpaceOwnership> Spaces { get; private set; } = [];

    public string? Error { get; private set; }

    public string? Success { get; private set; }

    public AdministrationModel(AppOptions appOptions, IsolationSpaceService spaceService)
    {
        this.appOptions = appOptions;
        this.spaceService = spaceService;
    }

    public void OnGet() => Spaces = [.. spaceService.ListAll()];

    public async Task<IActionResult> OnPostDeleteAsync(string name, CancellationToken cancellationToken)
    {
        var deleted = await spaceService.DeleteAnyAsync(name, cancellationToken);
        Error = deleted ? null : "删除失败：该空间不存在。";
        Success = deleted ? "隔离空间已删除。" : null;
        Spaces = [.. spaceService.ListAll()];
        return Page();
    }
}
